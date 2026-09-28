# Architecture decision records

These are the decisions behind the .NET 10 migration described in [MIGRATION_PLAN.md](MIGRATION_PLAN.md). Each ADR is added in the same commit as the change it describes.

- ADRs are numbered in order, and numbers are never reused.
- An accepted ADR is not rewritten. When a decision changes, a new ADR supersedes it, and the old one stays in place with its status updated.
- Facts cited in an ADR link to their evidence: the audit ([docs/legacy-audit.md](docs/legacy-audit.md)), the characterization data ([docs/legacy/](docs/legacy/README.md)) or the code.

## Index

| ADR | Title | Status | Plan stage |
|---|---|---|---|
| [ADR-0001](#adr-0001-migration-scope) | Migration scope | Accepted | 1.3 |
| [ADR-0002](#adr-0002-wire-contract-policy) | Wire-contract policy | Accepted | 1.3 |
| [ADR-0003](#adr-0003-non-goals) | Non-goals | Accepted | 1.3 |
| [ADR-0004](#adr-0004-write-endpoints-stay-anonymous-until-after-cutover) | Write endpoints stay anonymous until after cutover | Accepted | 1.3 |
| [ADR-0005](#adr-0005-migration-strategy-side-by-side-then-cutover) | Migration strategy: side-by-side, then cutover | Accepted | 2.1 |
| [ADR-0006](#adr-0006-solution-structure-and-build-conventions) | Solution structure and build conventions | Accepted | 2.1 |
| [ADR-0007](#adr-0007-test-strategy) | Test strategy | Accepted | 2.2 |
| [ADR-0008](#adr-0008-continuous-integration) | Continuous integration | Accepted | 2.3 |
| [ADR-0009](#adr-0009-configuration) | Configuration | Accepted | 3.1 |

## Template

```markdown
## ADR-NNNN: <decision as a short noun phrase>

- **Status:** Proposed | Accepted | Superseded by ADR-NNNN
- **Date:** YYYY-MM-DD
- **Plan stage:** <sub-task number>

### Context
The forces behind the decision, with links to the evidence.

### Decision
What we do, stated so that it can be checked.

### Alternatives considered
Each option that lost, and why.

### Consequences
What gets easier, what gets harder, the follow-up work, and the risks we accept.
```

---

## ADR-0001: Migration scope

- **Status:** Accepted
- **Date:** 2026-09-27
- **Plan stage:** 1.3

### Context

The legacy app has two HTTP surfaces ([audit: endpoint inventory](docs/legacy-audit.md#4-endpoint-inventory)):

- **Web API 2**, which is small:
  - `BrandsController`: `GET /api/brands`, `GET /api/brands/{id}`, and a `DELETE /api/brands/{id}` that does nothing.
  - `FilesController`: `GET /api/files`, which returns a BinaryFormatter stream.
- **MVC 5 with Razor views**, which holds everything else:
  - item list, details, create, edit and delete, which exist only as HTML forms
  - brand and type dropdowns
  - the picture route `GET /items/{catalogItemId:int}/pic`

The characterization run ([docs/legacy](docs/legacy/README.md)) shows how each of these behaves today. It also shows that several capabilities are reachable only through anti-forgery-protected HTML forms.

### Decision

The target is one ASP.NET Core Web API on .NET 10, covering the full catalog:

| Legacy capability | In the new API |
|---|---|
| `GET /api/brands`, `GET /api/brands/{id}`, `DELETE /api/brands/{id}` | Migrated drop-in ([ADR-0002](#adr-0002-wire-contract-policy)). The delete stays a no-op. |
| `GET /items/{catalogItemId:int}/pic` | Migrated with the same route and route name (`GetPicRouteTemplate`). |
| Item list, details, create, edit, delete (Razor only) | Re-exposed as REST: `GET /api/items` (paged), `GET /api/items/{id}`, `POST /api/items`, `PUT /api/items/{id}`, `DELETE /api/items/{id}`. |
| Type dropdown (Razor only) | Re-exposed as `GET /api/types`. |
| `GET /api/files` | Retired. It answers `410 Gone` and points to `/api/brands`. |
| Razor UI, bundles, client libraries, `CatalogController2` | Dropped. `CatalogController2` is unreachable today. |
| `eShopLegacy.Utilities` | Removed at cutover (Stage 11). Its only consumer is `/api/files`. |

Runtime features:

- **Kept:**
  - in-memory mode (`UseMockData`, which becomes `Catalog:UseMockData`)
  - a size-rolled log file with the log4net limits: 10 MB per file, 5 backups plus the active file
- **Dropped:**
  - the CSV customization seed (`UseCustomizationData`, `Setup/*.csv`, `Setup/CatalogItems.zip`)
  - Application Insights: no instrumentation key exists anywhere in the repository, so it sends nothing today
  - session state: only the Razor layout reads it

The legacy projects stay in the repository, unchanged, as the reference implementation until Stage 11. The `legacy-final` tag marks the rollback point.

### Alternatives considered

- **Port only the Web API 2 surface.** This loses every item capability, because they exist only behind Razor.
- **Port the UI as well (Razor Pages or MVC on ASP.NET Core).** The migration is API-focused. A UI can be built later on top of the new endpoints without touching them.

### Consequences

- The re-exposed endpoints have no legacy wire contract to match. They follow the conventions of the migrated endpoints ([ADR-0002](#adr-0002-wire-contract-policy)) and the business rules recorded in [`docs/legacy/evidence`](docs/legacy/README.md#evidence). Their shape is designed in Stages 7.5–7.7.
- UI-only mechanics disappear with the UI: anti-forgery tokens, redirects after posts, HTML validation messages, and the session cookie.
- Dropping `/api/files` removes the last BinaryFormatter use, which is what allows `eShopLegacy.Utilities` to go.

---

## ADR-0002: Wire-contract policy

- **Status:** Accepted
- **Date:** 2026-09-27
- **Plan stage:** 1.3

### Context

Clients of the Web API 2 routes should not have to change when the backend moves to .NET 10. The characterization ([contract coverage](docs/legacy/README.md#contract-coverage)) shows what "the same" means in practice:

- JSON property names are PascalCase (the Newtonsoft default in Web API 2).
- Content negotiation returns XML for `application/xml`, `text/xml` and typical browser `Accept` headers.
- Error bodies are framework output: Web API `{"Message", "MessageDetail"}` or empty for the API, and IIS or ASP.NET HTML pages elsewhere.
- Some responses come from IIS, not the app: `OPTIONS`, and a path segment that contains a dot.
- Accidental features exist, such as `GET /api/brands?id=2` returning one brand.

### Decision

1. **Drop-in compatibility for migrated endpoints.** Routes (case-insensitive, trailing slash tolerated), verbs, status codes, JSON property names, JSON shapes and ordering stay the same. `DELETE /api/brands/{id}` stays a no-op: 200 for an existing brand, 404 for an unknown one.
2. **PascalCase JSON on every endpoint**, including the re-exposed ones, so that the API is consistent.
3. **Only forced deltas.** A behaviour may differ from the golden exchanges only when one of these forces it:
   - **Security.** Examples: retiring `/api/files`, and the picture-endpoint fixes.
   - **The platform.** Examples: no XML content negotiation, and IIS-level behaviour that Kestrel does not have.

   Every delta gets an entry in [docs/behavior-changes.md](docs/behavior-changes.md) in the commit that introduces it. The entry names the golden exchange it departs from, and a test asserts the new behaviour.
4. **Status codes are contract; error payloads are not.** The legacy error bodies are framework output. The new error format is chosen in Stage 7.1 and recorded as a delta.
5. **Accidental behaviours are decided one at a time.** Behaviour that comes from the Web API 2 action selector or IIS rather than from the code (for example, the `?id=` query-string binding) is decided in the stage that ports the endpoint. If it is not reproduced, it is recorded as a delta.
6. **No idiomatic changes during the migration.** camelCase, real brand deletion, `201`/`204` semantics and similar improvements are deferred to a future v2 of the API.
7. **Verification.** The golden exchanges in [docs/legacy/contract](docs/legacy/contract) are replayed against the new API using the [comparison rules](docs/legacy/README.md#comparison-rules).

### Alternatives considered

- **Idiomatic now** (camelCase, ProblemDetails everywhere, proper REST semantics). This breaks every existing client during a platform change, and mixes two kinds of risk in one release.
- **Byte-for-byte parity**, headers and XML included. This would need controllers with XML formatters and emulation of IIS behaviour. The headers it would preserve (`Server`, `X-Powered-By`, `X-AspNet-Version`, `X-AspNetMvc-Version`) disclose the stack, so keeping them has negative value.

### Consequences

- Some legacy oddities are kept on purpose: the no-op delete, and PascalCase on new endpoints.
- Every departure is visible in one register, and each one has a test.
- The error-format change is a known, recorded delta, not an accident discovered by a client.
- v2 has a ready list of idiomatic changes to start from.

---

## ADR-0003: Non-goals

- **Status:** Accepted
- **Date:** 2026-09-27
- **Plan stage:** 1.3

### Context

A migration with an open-ended scope does not finish. Several legacy features are unused, UI-only or unsafe, and some improvements are tempting but belong in a separate change. Each exclusion below is explicit, and each one names what would bring it back, so that it can be revisited on evidence rather than by default.

### Decision

These are out of scope for the migration:

| Non-goal | Why it is out | What would bring it back |
|---|---|---|
| Porting the Razor/MVC UI | The migration is API-focused ([ADR-0001](#adr-0001-migration-scope)). | A product need for a server-rendered admin UI. It would be built as a separate client of the API. |
| XML content negotiation | Minimal APIs return JSON only, and no known client needs XML. | Evidence of a real XML consumer, such as access logs showing `Accept: application/xml` traffic. |
| Any BinaryFormatter payload | BinaryFormatter is unsafe and removed from .NET 9+. `/api/files` is retired. | Nothing brings BinaryFormatter back. A need for a bulk export would be met with a new JSON or CSV endpoint. |
| The CSV customization seed (`UseCustomizationData`) | It is off in the committed config. When on, it deletes and re-extracts the `Pics` folder at startup. | A requirement to seed a different catalog per deployment. It would be built as an import tool or a seeding option with its own tests. |
| Application Insights | No instrumentation key exists, so the legacy telemetry is inert. | An operational requirement for APM. It would use OpenTelemetry with an exporter, not the classic SDK. |
| Session state | Only the Razor layout reads it: it shows the machine name and the session start time. | None expected. The API is stateless. |
| The log4net line format | Serilog replaces log4net (Stage 6). Only the file location and rolling limits are kept. | A downstream tool that parses the old format. |
| Reproducing IIS host behaviour (`OPTIONS` answered by IIS, dotted segments served by the static file handler, stack-disclosure headers) | It is host behaviour, not app behaviour, and the new host is Kestrel. | A client that demonstrably depends on one of these. |
| Idiomatic contract changes (camelCase, real brand delete, `201`/`204`) | Deferred to v2 ([ADR-0002](#adr-0002-wire-contract-policy)). | A decision to version the API. |
| A YARP or `SystemWebAdapters` strangler setup | About ten endpoints do not justify the proxy machinery. The Stage 2.1 ADR covers the migration strategy. | A larger surface, or a need to migrate endpoint by endpoint in production. |

### Alternatives considered

- **Leave non-goals implicit.** Excluded features then come back as surprise work in later stages, or are silently lost with no record.

### Consequences

- The dropped features are documented, with evidence, in the audit and here.
- Each row names a trigger, so the list can be revisited when circumstances change.

---

## ADR-0004: Write endpoints stay anonymous until after cutover

- **Status:** Accepted
- **Date:** 2026-09-27
- **Plan stage:** 1.3

### Context

The legacy app has no authentication or authorization anywhere ([audit: defects and risks](docs/legacy-audit.md#7-defects-and-risks)):

- Anyone who can reach the MVC forms can create, edit and delete items.
- The anti-forgery tokens protect only against cross-site request forgery. They do not identify anyone.
- The Web API `DELETE /api/brands/{id}` needs no identity, and it does nothing anyway.

The new API exposes the item writes directly (`POST`, `PUT` and `DELETE` on `/api/items`). Adding authentication during the migration would change the contract of the migrated endpoints and mix a security feature into a platform change.

### Decision

- The migration keeps the legacy posture: no authentication on any endpoint. This is an **accepted risk** until Stage 12.
- Stage 12, after cutover, adds JWT bearer authentication with a `catalog:write` scope policy on item `POST`, `PUT` and `DELETE`. Local tokens come from `dotnet user-jwts`. Reads and `/api/brands` stay anonymous.
- Until Stage 12, the new API must not be deployed where untrusted clients can reach it.

### Alternatives considered

- **Add authentication in Stage 7 with the write endpoints.** This mixes two changes and makes the contract comparison against legacy harder. It also needs a token story for every test from the start.
- **Keep the writes out of the API until Stage 12.** Stages 7–11 would then be unable to prove that the re-exposed capabilities work.

### Consequences

- Until Stage 12, anyone who can reach the new API can change the catalog, as with the legacy UI today.
- Browser-based cross-site writes remain hard: the new API uses no cookies (no ambient credentials to abuse), it accepts JSON bodies (which need a CORS preflight), and it enables no CORS.
- Stage 12 has a defined scope, and its tests (401, 403, 2xx) are planned.

---

## ADR-0005: Migration strategy: side-by-side, then cutover

- **Status:** Accepted
- **Date:** 2026-09-27
- **Plan stage:** 2.1

### Context

- The legacy app is one System.Web application. Web API 2 and MVC 5 share the IIS integrated pipeline, `Global.asax` drives startup, and Autofac, log4net and EF6 are wired through it ([audit: architecture](docs/legacy-audit.md#2-architecture-and-request-pipeline)). System.Web has no .NET 10 equivalent.
- The plan requires every commit to build and pass its tests on its own.
- The HTTP surface is small: about ten endpoints once the Razor-only capabilities are re-exposed ([ADR-0001](#adr-0001-migration-scope)).
- The golden exchanges and the defect evidence were captured from the running legacy app ([docs/legacy](docs/legacy/README.md)). Until the new API matches them, the legacy app is the reference, and it must stay runnable so that a capture can be repeated.
- The two apps need different toolchains: Visual Studio's MSBuild on Windows for the legacy app ([audit: build](docs/legacy-audit.md#8-build-tooling-and-tests)), and the .NET 10 SDK for the new one.

### Decision

1. **A new project next to the old ones.** The new API is `src/eShop.Catalog.Api`, in its own solution `eShop.Catalog.slnx` ([ADR-0006](#adr-0006-solution-structure-and-build-conventions)). The legacy projects and `eShopLegacyMVC.sln` are not edited until Stage 11. The only files added beside them are the stop-files of ADR-0006.
2. **No runtime coexistence.** There is no reverse proxy, no `SystemWebAdapters` and no shared database. The new API gets its own database. Adopting an existing legacy database is a separate, tested procedure (Stage 4.3).
3. **One cutover step, in Stage 11.** The `legacy-final` tag marks the rollback point: the last commit where the legacy app is complete and runnable. After it, the pictures move into the API project, and the legacy projects, their solution and their stop-files are deleted.
4. **Rollback** means redeploying the `legacy-final` tag. Stage 11.1 records the rollback procedure, including which database the legacy app runs against.
5. **The legacy build stays green until cutover.** A commit that changes a repo-wide build file also runs the legacy MSBuild build, and builds the Stage 1.2 capture tool (`dotnet build docs/legacy/capture/capture.cs`).

### Alternatives considered

- **In-place conversion.** This means retargeting the legacy projects to SDK-style `net10.0`, by hand or with the .NET Upgrade Assistant. Nothing compiles until every System.Web dependency is gone, so there are no buildable intermediate commits. The reference implementation also disappears with the first commit.
- **Incremental strangler.** YARP sits in front of the IIS app, `Microsoft.AspNetCore.SystemWebAdapters` bridges the two, and routes move one at a time in production. This pays off for a large surface that must move gradually under live traffic. Here it would mean running and proxying two hosts to move about ten endpoints ([ADR-0003](#adr-0003-non-goals)). A single cutover, guarded by the drop-in contract ([ADR-0002](#adr-0002-wire-contract-policy)) and the contract replay tests, costs less.
- **A separate repository for the new API.** The history would no longer tell the migration as one story, and the characterization data would have to be copied and kept in sync.

### Consequences

- Two solutions and two toolchains coexist until Stage 11. Every command names its solution.
- The legacy code is frozen. Its defects are fixed only in the new API, and each fix that changes behaviour is recorded in [docs/behavior-changes.md](docs/behavior-changes.md).
- The golden exchanges can be re-captured from the legacy app at any point before cutover.
- Cutover is a single switch for clients. The drop-in contract ([ADR-0002](#adr-0002-wire-contract-policy)) and the contract replay tests are what make that switch safe.
- Until Stage 11.2 the new API serves the pictures from the legacy `Pics` folder, through `Catalog:PicturesPath` (Stage 7.4).

---

## ADR-0006: Solution structure and build conventions

- **Status:** Accepted
- **Date:** 2026-09-27
- **Plan stage:** 2.1

### Context

- The legacy projects are non-SDK-style and keep their package versions inline. They build only with Visual Studio's MSBuild, with 6 tolerated warnings and two vulnerable packages ([audit: build](docs/legacy-audit.md#8-build-tooling-and-tests), [packages](docs/legacy-audit.md#3-package-inventory)).
- The new code should start with strict defaults, so that no later stage has to retrofit them: nullable reference types, warnings as errors, and the async analyzers of the async-first port (plan decision 6).
- Three kinds of repo-wide file reach every project below them:
  - MSBuild imports the nearest `Directory.Build.props` above a project.
  - NuGet imports the nearest `Directory.Packages.props`.
  - `.editorconfig` files apply up to the first one marked `root = true`.

  Two kinds of code in the repository must not pick up the new settings, and files at the repository root reach both:
  - the legacy projects, which stay untouched until cutover ([ADR-0005](#adr-0005-migration-strategy-side-by-side-then-cutover))
  - the Stage 1.2 capture tool `docs/legacy/capture/capture.cs`, a .NET 10 file-based app that pins its package inline (`#:package Microsoft.Data.SqlClient@6.1.1`) and stays with `docs/legacy` after cutover

  This was checked. Without the stop-files below, both fail with `NU1008`, because Central Package Management forbids inline versions. Past that, the capture tool fails with dozens of analyzer and style errors from the new settings.

### Decision

**Layout**

| Path | Purpose |
|---|---|
| `eShop.Catalog.slnx` | The new solution, in the XML `.slnx` format. |
| `src/eShop.Catalog.Api` | The one API project. It is organized by feature folders (`Health/` now; brands, items and the rest as they arrive). Its types are `internal` unless something outside the assembly needs them. |
| `tests/eShop.Catalog.Api.UnitTests`, `tests/eShop.Catalog.Api.IntegrationTests` | The test projects (Stage 2.2). |

**Repo-wide build files**

| File | What it sets |
|---|---|
| `global.json` | SDK 10.0.100 or a later 10.0 feature band (`rollForward: latestFeature`), no previews. `dotnet test` runs on Microsoft.Testing.Platform. |
| `Directory.Build.props` | <ul><li>`net10.0`, nullable, implicit usings.</li><li>Compiler, analyzer, NuGet and MSBuild warnings as errors, except the NuGet audit warnings NU1900–NU1905 (an advisory, or vulnerability data that could not be fetched).</li><li>Analyzers pinned to `AnalysisLevel` 10.0 in `Recommended` mode, and code style enforced at build.</li><li>NuGet audit of direct and transitive packages at every severity.</li><li>`ContinuousIntegrationBuild` on CI.</li></ul> |
| `Directory.Packages.props` | Central Package Management with transitive pinning. A version is added in the commit that adds its first consumer. |
| `nuget.config` | nuget.org as the only source, with package source mapping. It applies to both solutions. |
| `.editorconfig` | Formatting (IDE0055), style and naming rules, all enforced at build. The async analyzers CA2016, CA1849 and CA2012 are errors. |

**Stop-files.** `src/eShopLegacyMVC`, `src/eShopLegacy.Utilities` and `docs/legacy/capture` each get three files:

- an empty `Directory.Build.props`
- a `Directory.Packages.props` that turns Central Package Management off
- an `.editorconfig` with `root = true`

They are the only files added to the legacy project folders and to the capture tool's folder. Stage 11.3 deletes the ones in the legacy project folders with the projects. The ones in `docs/legacy/capture` stay, because `docs/legacy` stays.

**Rules**

- Every command names its solution: `dotnet build eShop.Catalog.slnx`, `MSBuild.exe eShopLegacyMVC.sln`.
- A commit that changes a repo-wide build file also runs the legacy MSBuild build and `dotnet build docs/legacy/capture/capture.cs`.
- Shared settings go in `Directory.Build.props`. A project file holds only what is specific to that project.
- An analyzer rule is relaxed only in `.editorconfig`, for the narrowest set of files, with a comment that says why.

**Verification at 2.1**

- `dotnet build eShop.Catalog.slnx`: 0 warnings, 0 errors.
- `MSBuild.exe eShopLegacyMVC.sln -restore -t:Rebuild`: 6 warnings, 0 errors, the same result as the audit.
- `dotnet build docs/legacy/capture/capture.cs`: builds, as before this change.
- Throwaway probe projects confirmed the enforcement:
  - IDE0055, IDE0161, IDE1006, IDE0044, CS0649, CA2016, CA1849 and an MSBuild warning (MSB9999) fail the build.
  - NU1903 (Newtonsoft.Json 12.0.1) stays a warning.

### Alternatives considered

- **One project per layer** (API, application, domain, infrastructure). About ten endpoints over one small model do not repay the extra project boundaries. Folders and `internal` visibility give the separation that matters. A second host that needs the domain, such as a worker, would reopen this.
- **A `.sln` file.** `.slnx` is what the .NET 10 CLI creates by default. It is readable XML that merges cleanly, and Visual Studio 2026 opens it. The legacy solution stays a `.sln`, untouched.
- **`AnalysisLevel` `latest`.** Any SDK update could then add new errors with no code change. Pinning makes analyzer upgrades a deliberate commit.
- **NuGet audit warnings as errors.** A newly published advisory would break every build, including pull requests that do not touch packages. So would a vulnerability feed that cannot be reached (NU1900), even though every package restores. The CI vulnerable-package check (Stage 2.3) reports advisories instead.
- **Moving the legacy projects into a `legacy/` folder** to isolate them by directory. This edits the legacy tree and breaks the paths that the audit and the characterization data cite.
- **Opting out inside the legacy project files** (`ImportDirectoryBuildProps=false` and similar). This edits legacy files, which ADR-0005 rules out.
- **NuGet lock files** (`packages.lock.json`). Central versions fix every direct package. NuGet resolves each transitive package to the lowest version the graph allows, so an unchanged graph does not drift. nuget.org packages are immutable, and source mapping allows only nuget.org. Lock files would add churn to every package change. A second package source would reopen this.

### Consequences

- The new code starts strict: nullable, warning-free and async-checked from the first commit.
- Upgrading the analyzer rule set is a deliberate change of `AnalysisLevel`.
- New vulnerability advisories do not break local builds, so the CI check has to catch them.
- Six stop-files live in the legacy project folders until Stage 11.3. Three more stay in `docs/legacy/capture` for good.
- Any other code later placed under the repository root, outside the solution, gets the new settings unless it is given stop-files too.
- Contributors need the .NET 10 SDK for the new solution, and Visual Studio 2026 for the legacy one.

---

## ADR-0007: Test strategy

- **Status:** Accepted
- **Date:** 2026-09-27
- **Plan stage:** 2.2

### Context

- The legacy app has no tests. Its behaviour is pinned by the Stage 1.2 characterization, not by code ([audit: tests](docs/legacy-audit.md#84-tests)).
- Plan decision 8 sets the direction:
  - xUnit v3 on Microsoft.Testing.Platform (MTP)
  - `WebApplicationFactory` through one shared factory
  - Testcontainers SQL Server, with one container per test assembly and one database per test class
  - no EF Core InMemory provider and no SQLite, because neither can run the `catalog_hilo` sequence
- The .NET 10 SDK runs MTP test projects natively once `global.json` selects the MTP runner ([ADR-0006](#adr-0006-solution-structure-and-build-conventions)). xUnit v3 4.0 supports only MTP v2, and the `xunit.v3` package brings it in.
- Some logic can be tested without a host: policies, validation, paging, model metadata. Most of what the migration promises, though, is HTTP behaviour ([ADR-0002](#adr-0002-wire-contract-policy)), and that can only be tested through a host.

### Decision

**Framework.** xUnit v3 (`xunit.v3` 4.0.1). The test projects are executables that run on MTP v2, whether started by `dotnet test`, by an IDE or directly (`UseMicrosoftTestingPlatformRunner`). `dotnet test --solution eShop.Catalog.slnx` runs them all. There is no `Microsoft.NET.Test.Sdk` and no VSTest adapter. `tests/Directory.Build.props` holds what the test projects share: the executable output type, the MTP runner, the `xunit.v3` reference and a global `using Xunit;`.

**Two test projects**

| Project | What it tests | Needs |
|---|---|---|
| `tests/eShop.Catalog.Api.UnitTests` | Logic that runs without a host and without I/O. | Only the SDK. |
| `tests/eShop.Catalog.Api.IntegrationTests` | <ul><li>Behaviour through HTTP, against the app hosted in memory by `CatalogApiFactory`.</li><li>From Stage 4.2, persistence behaviour against a real SQL Server in Testcontainers: migrations, schema, seeding, and the service contract suite. These tests reach the database through the factory's services.</li></ul> | Docker, from Stage 4.2. |

Both projects can see the API's `internal` types (`InternalsVisibleTo`). The API keeps its types internal ([ADR-0006](#adr-0006-solution-structure-and-build-conventions)), and the persistence tests need the `DbContext`, the seeder and the services.

**`CatalogApiFactory`** is the one `WebApplicationFactory<Program>` subclass.

- Each integration test class receives it through `IClassFixture<CatalogApiFactory>`. The tests of one class share one host, and each class gets its own. Stage 4.2 attaches the per-class database here.
- The host runs in the `Testing` environment. User secrets and Development-only features, such as Swagger UI in Stage 9, stay off unless a test turns them on.
- A test that needs a different host configuration derives one with `WithWebHostBuilder` and disposes it.

**Conventions**

- HTTP tests call routes by their literal path, such as `/health/live`, and never through constants from the API. Routes are contract ([ADR-0002](#adr-0002-wire-contract-policy)), so a route change must break a test.
- One test class per subject, named `<Subject>Tests`, in a folder that mirrors the API's feature folder.
- Test method names are sentences with underscores, for example `Get_returns_200_Healthy_as_plain_text`. The analyzer rule against underscores (CA1707) is turned off for `tests/` only, in `tests/.editorconfig`.
- Assertions use xUnit's `Assert`.
- Tests are async all the way down. Every call that takes a `CancellationToken` gets `TestContext.Current.CancellationToken`. The xUnit analyzer xUnit1051 fails the build otherwise.

**First tests**

| Test | What it pins |
|---|---|
| `LivenessPolicyTests.Liveness_runs_no_registered_check` (unit) | The liveness predicate excludes every registered health check. |
| `LivenessEndpointTests.Get_returns_200_Healthy_as_plain_text` (integration) | `GET /health/live` answers `200`, `text/plain`, `Healthy`. |
| `LivenessEndpointTests.Get_stays_healthy_when_a_registered_check_fails` (integration) | The endpoint applies that predicate, so a failing dependency cannot fail liveness. |

Each of the two policy tests was checked against a deliberate break: including all checks in the predicate fails the unit test, and mapping the endpoint without the liveness options fails the second integration test.

### Alternatives considered

- **VSTest** (`Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio`). xUnit v3 4.0 targets MTP v2 by default, and the .NET 10 SDK runs MTP natively once `global.json` opts in, which it does. VSTest would need adapter packages and a separate test host process. MTP test projects are plain executables that can also be run or debugged directly.
- **NUnit or MSTest.** Both run on MTP. xUnit v3 is the plan's choice (plan decision 8). Its class and assembly fixtures fit the per-class host and database, and its per-test `TestContext` carries a cancellation token.
- **One test project, split by traits.** Separate projects keep host-only packages, such as `Microsoft.AspNetCore.Mvc.Testing`, out of the unit tests, and they make "run only the fast tests" a project choice rather than a filter. The Docker trait of Stage 10.2 still splits the integration tests.
- **One host for the whole assembly** (an xUnit assembly fixture). It starts faster, but per-class databases and per-class host customization are simpler with one host per class. This can be revisited if host startup starts to dominate the test time.
- **A third-party assertion library** (FluentAssertions, Shouldly). FluentAssertions 8 needs a paid licence for commercial use, and xUnit's `Assert` covers what the tests need without another dependency.

### Consequences

- `dotnet test --solution eShop.Catalog.slnx` is the single command for every test, locally and in CI.
- Until Stage 4.2 no test needs Docker. From then on the integration tests do, and Stage 10.2 documents a Docker-free subset.
- HTTP behaviour is pinned through literal routes. Persistence behaviour is tested against a real SQL Server, never an in-memory substitute.
- The factory is the one place that later stages change to add the database, configuration overrides or authentication (Stage 12).

---

## ADR-0008: Continuous integration

- **Status:** Accepted
- **Date:** 2026-09-27
- **Plan stage:** 2.3

### Context

- From Stage 2.3 on, a pull request should merge into `main` only when the new solution builds and its tests pass.
- Local builds keep NuGet audit warnings as warnings on purpose ([ADR-0006](#adr-0006-solution-structure-and-build-conventions)). Something else has to turn a vulnerable package into a failure.
- From Stage 4.2 the integration tests start SQL Server in a Linux container ([ADR-0007](#adr-0007-test-strategy)). GitHub-hosted Linux runners have Docker. Windows runners cannot run Linux containers.
- The legacy solution builds only on Windows, with Visual Studio's MSBuild and the .NET Framework 4.6.1 targeting pack ([audit: toolchain](docs/legacy-audit.md#83-toolchain-prerequisites)).
- Workflow steps run with a token that can read the repository. A third-party action referenced by a tag can change under the workflow, because tags can be moved.

### Decision

One workflow, [`.github/workflows/ci.yml`](.github/workflows/ci.yml). It runs on every pull request, on every push to `main`, every Monday at 05:17 UTC, and on demand.

**Job "Build and test"** (`ubuntu-24.04`)

1. Install the SDK that `global.json` selects.
2. Restore, then build `eShop.Catalog.slnx` in Release. Warnings are errors ([ADR-0006](#adr-0006-solution-structure-and-build-conventions)).
3. Build the Stage 1.2 capture tool (`dotnet build docs/legacy/capture/capture.cs`). This checks that its stop-files still keep the repo-wide build settings away from it.
4. Run `dotnet test --solution eShop.Catalog.slnx` with a TRX report per test project.
5. Upload the TRX files as the `test-results` artifact, also when tests fail.

**Job "Vulnerable packages"** (`ubuntu-24.04`)

1. List every vulnerable package, direct or transitive, for the log.
2. Restore with the NuGet audit warnings NU1900–NU1905 turned back into errors. The job fails on an advisory of any severity, and when the vulnerability data cannot be fetched.

The weekly run catches an advisory published for a package that no commit has touched. An advisory with no fixed version can be accepted with a `NuGetAuditSuppress` item, which the gate honours. Each suppression carries a comment with the reason and a date to review it.

**Hardening**

- The workflow token can only read the repository contents (`permissions: contents: read`), and the checkout does not keep it (`persist-credentials: false`).
- Every action is pinned to a full commit SHA, with the release tag in a comment.
- A new push to a pull request cancels the run still going for its previous commit.
- Every job has a timeout.

**Not in CI**

- The legacy MSBuild build. It needs Windows and Visual Studio, and it is checked locally whenever a repo-wide build file changes ([ADR-0006](#adr-0006-solution-structure-and-build-conventions)).
- Running the capture tool. That needs Windows, IIS Express and LocalDB. Only its build is checked.
- Coverage and published test reports (Stage 10).

"Build and test" and "Vulnerable packages" are meant to be required status checks for `main`. That is a repository setting on GitHub, outside the code.

**Verification at 2.3.** Every command of both jobs was run locally with `CI=true`: the Release build had 0 warnings, 3 of 3 tests passed with a TRX file per project, and the gate passed. The capture tool also builds for `linux-x64`, the runner's platform. The gate was also run against a probe project that references Newtonsoft.Json 12.0.1: it failed with `error NU1903`, and it passed once the advisory was listed in `NuGetAuditSuppress`.

### Alternatives considered

- **`ubuntu-latest`.** The label moves to a new image without notice. A pinned image makes that upgrade a visible commit.
- **A Windows job for the legacy build.** GitHub's Windows images carry a different Visual Studio from the one the audit used, and it is not certain that they have the .NET Framework 4.6.1 targeting pack. A red legacy job could block every pull request for reasons that have nothing to do with the change. The legacy build is frozen, checked locally, and gone after cutover.
- **`dotnet package list --vulnerable` as the gate.** It exits with 0 even when it finds advisories, so it cannot fail a job by itself. A script that failed on its output would still count the advisories accepted with `NuGetAuditSuppress`, because the command ignores them, and it would stay red for good. Both behaviours were checked. The job still runs the command, for its readable table.
- **NuGet audit warnings as errors in the build itself.** [ADR-0006](#adr-0006-solution-structure-and-build-conventions) rejected this because every build, pull requests included, would break on a new advisory or an unreachable feed. The separate gate has the same effect on merges while it is a required check (see Consequences), but it is one isolated check: local builds keep working, and "Build and test" still reports on the change itself.
- **Actions pinned by tag** (`@v6`). A tag can be moved to another commit. A commit SHA cannot.
- **NuGet package caching.** Restore takes seconds for this solution, and `setup-dotnet` keys its cache on lock files, which ADR-0006 does not use.

### Consequences

- Every pull request shows whether the new solution builds, whether its tests pass, and whether any package is vulnerable.
- A new advisory can turn `main` red without a code change. While "Vulnerable packages" is a required check, it also blocks every pull request, including those that touch no package, until a package update or a documented suppression lands on `main`. An unreachable vulnerability feed (NU1900) blocks merges until a re-run passes.
- GitHub disables the schedule of a public repository after 60 days without activity. After a quiet period, the weekly run has to be re-enabled from the Actions tab.
- Action and runner upgrades are deliberate commits that change a SHA or an image name.
- The legacy build stays a local check until Stage 11 removes it.

---

## ADR-0009: Configuration

- **Status:** Accepted
- **Date:** 2026-09-28
- **Plan stage:** 3.1

### Context

- The legacy app reads its settings from `Web.config` through `ConfigurationManager` ([audit: `Web.config`](docs/legacy-audit.md#61-webconfig)). Its code reads two of the app settings, each parsed with `bool.Parse` where it is used ([audit: appSettings consumers](docs/legacy-audit.md#62-appsettings-consumers)):
  - A missing or malformed `UseMockData` throws at the start of `Application_Start`, before any route is registered.
  - A missing or malformed `UseCustomizationData` throws at its end, when the database initializer is resolved, and only in database mode.
- Its one connection string points at the LocalDB database `Microsoft.eShopOnContainers.Services.CatalogDb`, with MARS on. MARS is unused: no query reads while another reader is open ([audit: queries](docs/legacy-audit.md#55-queries)).
- The legacy database belongs to the reference app until cutover. The new API gets a database of its own ([ADR-0005](#adr-0005-migration-strategy-side-by-side-then-cutover), decision 2). From Stage 4.4 the new API applies migrations and seeds data at startup in Development, so pointing it at the legacy database by mistake would change the reference app's data.
- `Web.config` pins `en-US` as the culture of every request, and the legacy price binding and validation silently depend on it ([audit: culture](docs/legacy-audit.md#63-culture-dependence-en-us-pinned-webconfig38)).
- `WebApplication.CreateBuilder` layers configuration from these sources, each overriding the ones before it:
  1. `appsettings.json`
  2. `appsettings.{Environment}.json`
  3. `{ApplicationName}.settings.json` and `{ApplicationName}.settings.{Environment}.json`, which .NET 10 adds (here `eShop.Catalog.Api.settings.json`). This project does not use them.
  4. user secrets, in Development only
  5. environment variables
  6. the command line
- The integration tests run the host in the `Testing` environment ([ADR-0007](#adr-0007-test-strategy)). From Stage 4.2 each test class gets its own database in a container.

### Decision

**1. What goes where**

| Source | Holds | In the repository |
|---|---|---|
| `appsettings.json` | Settings that every environment shares, with their defaults. It starts empty: each setting is added in the commit that adds the code that reads it. | Yes |
| `appsettings.Development.json` | Local development values that are not secret. Today that is the LocalDB connection string. | Yes |
| User secrets | A developer's own overrides and development credentials, for example a connection string to SQL Server in a container (Stage 4.5) or the `dotnet user-jwts` signing keys (Stage 12). They live in the user profile, in plain text. The host reads them only in Development. | No |
| Environment variables, command line | Everything a deployed environment needs, above all its connection string (`ConnectionStrings__CatalogDb`). A secret store is chosen when a deployment target exists. | No |
| `CatalogApiFactory` | Test settings, set in code. The `Testing` environment has no settings file. From Stage 4.1 the factory sets the connection string. Until Stage 4.2 it is a placeholder that no test connects to. After that, it points at each test class's own database. | Yes, as code |

No committed file holds a credential or a value for a deployed environment. The project's `UserSecretsId` is in `eShop.Catalog.Api.csproj`.

**2. The connection string**

`ConnectionStrings:CatalogDb` is set in `appsettings.Development.json` only:

```text
Data Source=(localdb)\MSSQLLocalDB;Initial Catalog=eShopCatalog;Integrated Security=True
```

- **A database of its own.** `eShopCatalog` is created by the new API's migrations (Stage 4.2). No default setting reaches the legacy database.
- **No password.** LocalDB uses Windows authentication.
- **No MARS** (the SqlClient default). Nothing needs it. Without it, code that starts a second query while a reader is still open fails at once, instead of multiplexing silently.
- **Encryption stays at the SqlClient default.** A probe with Microsoft.Data.SqlClient 6.1.1 connected to LocalDB with the default settings, so no `Encrypt` or `TrustServerCertificate` override is needed. The local named-pipe connection is not encrypted.
- **Nowhere else.** The `Testing` and `Production` environments get no connection string from the repository. From Stage 4.1 the host refuses to start without one.

A connection string is not an options class. It is read with `GetConnectionString("CatalogDb")`, the EF Core convention.

**3. Typed options**

Every other setting is read through a typed options class:

- One class per configuration section, `sealed` and `internal`, in the feature folder of the code that reads it, with a `SectionName` constant.
- Its rules are data annotations on its properties (`[Required]`, `[Range]` and so on).
- A setting with a safe default is a property initialized to that default. A setting without one is nullable or a reference type, and marked `[Required]`. `[Required]` on a non-nullable value type never fails: a missing key leaves the property at its default value, which counts as present.
- It is registered with `AddOptions<TOptions>().BindConfiguration(TOptions.SectionName).ValidateDataAnnotations().ValidateOnStart()`.
- The code that uses it takes `IOptions<TOptions>`.
- The class arrives in the commit that adds its first consumer, with a test that an invalid value stops the host at startup. The first ones are expected in Stage 4.4 (migrate on startup), Stage 5.3 (`Catalog:UseMockData`) and Stage 7.4 (`Catalog:PicturesPath`).

With `ValidateOnStart`, both kinds of bad value stop the host before it accepts requests. A malformed value fails binding, with an error that names the key. A missing required value fails validation, with an error that names the property. The legacy app instead failed part-way through startup, from a `bool.Parse` exception.

**4. Culture**

No culture is pinned. The wire contract is culture-invariant: System.Text.Json and Minimal API route and query binding do not use the current culture. Code that parses, formats or compares text names its culture: the analyzers CA1305 and CA1310 already fail the build otherwise. A probe project under the repository's build settings confirmed both. CA1305 does not look at string interpolation, so interpolated strings are not used for machine-readable values. The price rules that relied on `en-US` are ported as explicit culture-invariant checks in Stage 7.6.

**5. The fate of every `Web.config` element**

This extends the audit's [table](docs/legacy-audit.md#61-webconfig) with where each element goes.

| Element | What it does in the legacy app | In the new API |
|---|---|---|
| `configSections` (`entityFramework`) | Declares the EF6 configuration section. | Dropped with EF6. |
| `connectionStrings/CatalogDBContext` | LocalDB, database `Microsoft.eShopOnContainers.Services.CatalogDb`, MARS on. | `ConnectionStrings:CatalogDb` (decision 2): database `eShopCatalog`, MARS off, in `appsettings.Development.json` only. Read from Stage 4.1. |
| `appSettings/UseMockData` | Chooses the in-memory or the EF service, and skips the database initializer. | `Catalog:UseMockData`, a typed option (Stage 5.3). |
| `appSettings/UseCustomizationData` | Seeds from CSV files and a zip. | Dropped ([ADR-0003](#adr-0003-non-goals)). |
| `appSettings/webpages:Version`, `webpages:Enabled` | ASP.NET Web Pages settings for Razor. | Dropped with the UI. |
| `appSettings/ClientValidationEnabled`, `UnobtrusiveJavaScriptEnabled` | MVC client-side validation. | Dropped with the UI. |
| `system.web/compilation` (`debug="true"`, `targetFramework="4.7.2"`) | Compiles views and `Global.asax` at runtime in debug mode. Debug mode also turns off the execution timeout. | Dropped. The build configuration and `ASPNETCORE_ENVIRONMENT` take over its roles. |
| `system.web/httpRuntime` (`targetFramework="4.6.1"`) | Selects the 4.6.1 runtime behaviour, with the default limits: a 4 MB request body, and a 110-second execution timeout that `debug="true"` turns off. | Dropped. Kestrel's limits apply, and Kestrel has no execution timeout. The request-body limit is set with the first endpoint that reads a body (Stage 7.6). |
| `system.web/sessionState` (`InProc`) | Session state for the layout footer. | Dropped ([ADR-0003](#adr-0003-non-goals)). |
| `system.web/httpModules` | Registers the telemetry modules for the classic pipeline. The integrated pipeline ignores this section. | Dropped. |
| `system.web/globalization` (`culture`, `uiCulture` = `en-US`) | Sets the culture of every request. | Not carried over (decision 4). |
| `runtime/assemblyBinding` (10 redirects) | Unifies assembly versions. | Dropped. .NET 10 has no binding redirects, and Central Package Management fixes the versions ([ADR-0006](#adr-0006-solution-structure-and-build-conventions)). |
| `system.webServer/validation` | Stops IIS from rejecting the classic-mode `httpModules` section. | Dropped with IIS. |
| `system.webServer/modules` | Registers telemetry correlation, Application Insights and the async session-state module. | Dropped ([ADR-0003](#adr-0003-non-goals)). |
| `system.webServer/handlers` | Sends extensionless URLs to ASP.NET for GET, HEAD, POST, DEBUG, PUT, DELETE, PATCH and OPTIONS. Two of the three entries are for the classic pipeline only. | Dropped. Kestrel sends every request to the app. |
| `entityFramework/defaultConnectionFactory` | A LocalDB connection factory. It is never used, because the context names its connection string. | Dropped. |
| `entityFramework/providers` | Registers the EF6 SQL Server provider. | `UseSqlServer` in `AddDbContext` (Stage 4.1). |
| `system.codedom` | Roslyn compilers for the runtime compilation. | Dropped. |

The other configuration files of the legacy project:

| File | In the new API |
|---|---|
| `Web.Debug.config` (no active transform), `Web.Release.config` (removes `debug` when publishing) | Dropped. |
| `Views/Web.config` | Dropped with the UI. |
| `ApplicationInsights.config` | Dropped ([ADR-0003](#adr-0003-non-goals)). |
| `log4Net.xml` | Serilog settings in `appsettings.json` (Stage 6.1). |
| The IIS Express settings in `eShopLegacyMVC.csproj` | `Properties/launchSettings.json` (Stage 2.1). |

**Tests** (`tests/eShop.Catalog.Api.IntegrationTests/Configuration/ConfigurationTests.cs`)

| Test | What it pins |
|---|---|
| `Testing_host_gets_no_connection_string_from_settings_files` | No settings file that the test host reads sets the connection string: `appsettings.json` has none, and the test host does not read `appsettings.Development.json`. Values from environment variables or from the factory's code are not settings files, so the test still holds once the factory sets a connection string (Stage 4.1). |
| `Testing_host_reads_no_user_secrets` | The test host never reads a developer's secrets. |
| `Development_host_reads_user_secrets` | The project has a `UserSecretsId`, so a Development host reads user secrets. |
| `Development_settings_point_at_a_LocalDB_database_of_their_own_without_MARS` | The Development connection string: LocalDB, `eShopCatalog`, no MARS. It reads only the committed files, so a developer's own override cannot fail it. |

Each test was checked against a deliberate break, and each break failed the intended test:

- moving the connection string into `appsettings.json`
- removing the `UserSecretsId`
- running the factory in Development
- turning MARS on
- using the legacy database name

### Alternatives considered

- **The connection string in `appsettings.json`**, as the ASP.NET Core templates do. Every environment would inherit LocalDB. A deployment that forgot its own connection string would start and then fail on the first query. The integration tests would reach the developer's database whenever a test class forgot to attach its own.
- **The legacy database.** [ADR-0005](#adr-0005-migration-strategy-side-by-side-then-cutover) rules it out, and the migrations and seeding of Stage 4 would change the reference app's data.
- **MARS on, for parity.** Nothing uses it, and it would hide code that interleaves two readers on one connection.
- **A committed `appsettings.Production.json`.** Values for a deployed environment belong to that deployment, and there is no deployment target yet.
- **An `appsettings.Testing.json`.** Tests set their settings in code, next to the tests that depend on them.
- **Reading `IConfiguration` where a value is needed**, as the legacy code read `ConfigurationManager.AppSettings`. The values would be untyped and parsed at every use, and a bad value would fail the first request that reads it.
- **Source-generated options validation (`[OptionsValidator]`).** It serves trimming and Native AOT, which the API does not use. `ValidateDataAnnotations` needs no extra type per options class.
- **A pinned culture** (request localization with `en-US` only, or `CultureInfo.DefaultThreadCurrentCulture`). This would carry the legacy dependency over instead of removing it, and a pinned culture hides the code that depends on it.

### Consequences

- A deployed API must receive `ConnectionStrings__CatalogDb` or an equivalent. From Stage 4.1 it does not start without it.
- Once the API uses the database (Stage 4), `dotnet run` in Development needs LocalDB, which runs only on Windows. On Linux and macOS, a developer points the connection string at SQL Server in a container through user secrets (Stage 4.5).
- `appsettings.Development.json` and user secrets never reach the `CatalogApiFactory` host. Each test sets the settings it depends on. Environment variables still reach it, as they reach any host. Two of the configuration tests read the Development sources on purpose, outside that host.
- From Stage 4.1 the factory has to set a connection string, because the host does not start without one.
- Configuration errors stop the host at startup and name the setting, instead of failing the first request that reads it.
- The request-body limit is an open item for Stage 7.6, and the plan records it there.
