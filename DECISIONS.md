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
| [ADR-0010](#adr-0010-data-model) | Data model | Accepted | 4.1 |
| [ADR-0011](#adr-0011-ef-core-migration-strategy) | EF Core migration strategy | Accepted | 4.2 |
| [ADR-0012](#adr-0012-adopting-a-legacy-database) | Adopting a legacy database | Accepted | 4.3 |
| [ADR-0013](#adr-0013-seeding-migrate-on-startup-and-readiness) | Seeding, migrate-on-startup and readiness | Accepted | 4.4 |
| [ADR-0014](#adr-0014-local-development-databases) | Local development databases | Accepted | 4.5 |
| [ADR-0015](#adr-0015-async-first-catalog-service) | Async-first catalog service | Accepted | 5.1 |
| [ADR-0016](#adr-0016-in-memory-catalog-service) | In-memory catalog service | Accepted | 5.2 |
| [ADR-0017](#adr-0017-built-in-dependency-injection-and-mock-mode) | Built-in dependency injection and mock mode | Accepted | 5.3 |
| [ADR-0018](#adr-0018-logging-with-serilog) | Logging with Serilog | Accepted | 6.1 |
| [ADR-0019](#adr-0019-request-logging-and-application-log-events) | Request logging and application log events | Accepted | 6.2 |

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

---

## ADR-0010: Data model

- **Status:** Accepted
- **Date:** 2026-09-29
- **Plan stage:** 4.1

### Context

- The legacy app has three EF6 entities: `CatalogItem`, `CatalogBrand` and `CatalogType` ([audit: EF6 data model](docs/legacy-audit.md#5-ef6-data-model-and-expected-schema)). `CatalogItem` mixes storage with the UI:
  - MVC validation and display attributes (`[Required]`, `[Range]`, `[RegularExpression]`, `[Display]`, `[DataType]`)
  - a `PictureUri` that the mapping ignores and a controller fills for each request
  - a constructor that defaults `PictureFileName` to `dummy.png`
- The Stage 1.2 capture recorded the schema that EF6 creates ([`schema.json`](docs/legacy/schema.json)):
  - EF6 constraint names: `PK_dbo.Catalog`, `FK_dbo.Catalog_dbo.CatalogBrand_CatalogBrandId`, `IX_CatalogBrandId`
  - `decimal(18,2)` prices and cascading foreign keys
  - `IDENTITY` brand and type IDs
  - item IDs from the `catalog_hilo` sequence (`bigint`, start 1, increment 10), through a hand-rolled HiLo generator
- EF Core's conventions name the same objects differently: `PK_Catalog`, `FK_Catalog_CatalogBrand_CatalogBrandId`, `IX_Catalog_CatalogBrandId`.
- The legacy schema has two more sequences, `catalog_brand_hilo` and `catalog_type_hilo`. The seeding reads each once and discards the value, because brand and type IDs are `IDENTITY` ([audit: sequences](docs/legacy-audit.md#53-sequences-and-hilo)).
- Brand and type IDs are visible outside the database. `GET /api/brands` returns them, and the sample items refer to brand IDs 2 and 5 and type IDs 1–3 ([`seed-data.json`](docs/legacy/seed-data.json)).
- The [comparison rules](docs/legacy/README.md#schema-schemajson-used-from-stage-41) say which parts of the schema the new one must reproduce.
- Stage 4.3 lets the new API adopt an existing legacy database. That works without renaming anything only if the new schema uses the legacy names.

### Decision

**1. Entities** (`Catalog/`)

`CatalogItem`, `CatalogBrand` and `CatalogType` are `internal sealed` classes that hold only the stored data. They live in `Catalog/`, and the EF Core mapping in `Data/`, because the brands, items and types endpoints of Stage 7 all share them ([ADR-0006](#adr-0006-solution-structure-and-build-conventions) keeps a folder per feature).

- **No attributes.** Input validation belongs to the request contracts of the write endpoints (Stages 7.6 and 7.7). The display attributes went with the UI.
- **No `PictureUri`.** It is a URL computed for each response (Stage 7.5), not data.
- **No `dummy.png` default.** It is a rule of item creation, and it arrives with item creation (Stage 7.6).
- **Nullability follows the schema.** `Description` is the only nullable column (`string?`), and the other strings are `required`. The navigations `CatalogBrand?` and `CatalogType?` are nullable, because they are loaded only when a query includes them.

**2. Mapping** (`Data/`)

`CatalogDbContext` applies one `IEntityTypeConfiguration<T>` per entity. The mapping reproduces the legacy schema under the comparison rules:

- the table names, and the EF6 names of every primary key, foreign key and index
- `nvarchar(50)` for item names, `nvarchar(100)` for brand and type names, `decimal(18,2)` for prices, `nvarchar(max)` for the other strings
- cascading deletes on both foreign keys, as in the legacy schema (nothing deletes a brand or a type)
- the tables, the sequence and EF Core's `__EFMigrationsHistory` table all in `dbo`, named explicitly, so the default schema of the login that applies the migrations does not matter. `HasDefaultSchema` places the tables and the sequence. The history table is not part of the model, so the SQL Server options place it (decision 5).

**3. IDs**

- **Item IDs** use `UseHiLo("catalog_hilo")` on a `bigint` sequence that starts at 1 and increments by 10. EF Core's HiLo allocates blocks the same way as the legacy generator ([audit](docs/legacy-audit.md#53-sequences-and-hilo)), so a fresh database gives the sample items IDs 1–12 again.
- **Brand and type IDs** stay `IDENTITY(1,1)`.
- **`catalog_brand_hilo` and `catalog_type_hilo` are not modeled.** Nothing uses their values. A legacy database that still has them is handled by the Stage 4.3 baseline.

**4. Reference data**

Brands and types are seeded with `HasData`, with the legacy IDs and names. The seed becomes part of the migrations, so every database gets the same IDs, which clients and the sample items rely on. The sample items are not reference data. They are seeded at runtime (Stage 4.4), because their IDs come from HiLo.

**5. Registration**

`AddCatalogDbContext` registers the context with `AddDbContext` on `ConnectionStrings:CatalogDb`.

- **Shared options.** `UseCatalogSqlServer` holds the SQL Server options, so that the app, the tests and the design-time tooling of Stage 4.2 build the same context. Today it sets the provider and puts the migrations history table in `dbo`.
- **Fail fast.** A missing or blank connection string throws at startup. The message names the setting and where to set it ([ADR-0009](#adr-0009-configuration)).
- **No pooling, no retries.** There is no context pooling and no retry-on-failure. The legacy app had neither. A cloud database would reopen the retry question.
- **Application Name.** When the connection string sets no `Application Name`, EF Core 10 adds `EFCore/<version> (<OS>)`. The value is left as is.

**Tests**

Unit tests compare the EF Core design-time model with `schema.json` and `seed-data.json`. They need no database, only the two files that the build copies next to them.

| Test | What it pins |
|---|---|
| `CatalogModelTests.Model_maps_exactly_the_legacy_catalog_tables` | The model maps `dbo.Catalog`, `dbo.CatalogBrand` and `dbo.CatalogType`, and nothing else. |
| `CatalogModelTests.Table_matches_the_legacy_schema` (one case per table) | Columns, primary key, indexes, unique constraints, foreign keys and check constraints equal `schema.json` under the comparison rules. |
| `CatalogModelTests.Item_ids_come_from_the_legacy_hilo_sequence` | The only sequence is `catalog_hilo`, with the legacy type, start, increment, range and cycle setting, and the item ID uses it through HiLo. |
| `CatalogModelTests.Brands_are_seeded_with_their_legacy_ids`, `Types_are_seeded_with_their_legacy_ids` | The `HasData` rows equal `seed-data.json`. |
| `CatalogDbContextRegistrationTests.Registration_fails_when_the_connection_string_is_missing` | Registration throws, naming `ConnectionStrings:CatalogDb`, when no source sets it. |

Integration tests check the registration in the host:

| Test | What it pins |
|---|---|
| `CatalogDbContextRegistrationTests.DbContext_uses_the_configured_connection_string` | The context is configured with the factory's server and database. |
| `CatalogDbContextRegistrationTests.Migrations_history_table_is_in_dbo` | The registered context creates `[dbo].[__EFMigrationsHistory]`. |
| `CatalogDbContextRegistrationTests.Host_does_not_start_without_a_connection_string` (empty, blank) | The host fails at startup with a message that names `ConnectionStrings:CatalogDb`. |

The comparison uses schema facts: one line per column, key, index, foreign key, check constraint or sequence. The same format is produced from `schema.json` (`tests/Shared/Legacy`) and from the model (`tests/eShop.Catalog.Api.UnitTests/Data/EfModelSchema.cs`), and Stage 4.2 produces it from a live database as well. The [comparison rules](docs/legacy/README.md#schema-schemajson-used-from-stage-41) list what a fact contains.

Each test was checked against a deliberate break, and each break failed the intended test:

- a primary key renamed, or made nonclustered
- an index left with its EF Core convention name, or given a filter, an included column or a descending key
- `Restrict` instead of `Cascade`
- an identity column instead of HiLo for item IDs
- the item name length changed, or a collation set on it
- a default value on the price, or a computed column
- a check constraint added
- a brand renamed
- the default schema removed
- the sequence increment changed
- the history table left in the login's default schema

### Alternatives considered

- **EF Core's naming conventions.** Every primary key, foreign key and index name would differ from the legacy database. The schema comparison would need exceptions, and adopting a legacy database (Stage 4.3) would first have to rename its constraints.
- **Reverse engineering (`dotnet ef dbcontext scaffold`) from a legacy database.** It would reproduce the names and column types. It would also scaffold `__MigrationHistory` and the unused sequences, and it would take none of the decisions: HiLo, the `HasData` seed, the explicit `dbo` schema, nullable navigations. The model is small enough to write by hand, and the schema test checks it either way.
- **An `IDENTITY` column for item IDs.** It is simpler, but the legacy `Catalog.Id` is not an identity column, and SQL Server cannot add `IDENTITY` to an existing column. Adopting a legacy database (Stage 4.3) would mean rebuilding its `Catalog` table. With HiLo, an adopted database continues from its own sequence.
- **Keeping the brand and type sequences.** They would be dead objects in every new database.
- **Seeding brands and types at runtime, with the items.** Their IDs would depend on the insertion order in each database, as they did in the legacy app, where `IDENTITY` assigned them in list order.
- **Mapping with data annotations.** Attributes cannot name the constraints or declare the sequence, so the fluent configuration would be needed anyway, and the mapping would be split between two places.

### Consequences

- Every test run checks the EF Core model against the legacy schema, without a database. Stage 4.2 checks the database that the migrations create against the same file.
- A change to any compared part of the schema fails the tests until the comparison rules allow it.
- Validation, the picture URL and the default picture are not on the entity. Stages 7.5 to 7.7 implement them.
- The host needs a connection string in every environment, including `Testing`. `CatalogApiFactory` sets a placeholder until Stage 4.2 gives each test class a database ([ADR-0009](#adr-0009-configuration)). Stage 5.3 decides how mock mode, which needs no database, fits with this.

---

## ADR-0011: EF Core migration strategy

- **Status:** Accepted
- **Date:** 2026-09-29
- **Plan stage:** 4.2

### Context

- The legacy app creates its database with EF6's `CreateDatabaseIfNotExists` and three raw sequence scripts that hard-code the database name ([audit: sequences](docs/legacy-audit.md#53-sequences-and-hilo)). Nothing can change the schema of a database that already exists.
- The new API gets a database of its own ([ADR-0005](#adr-0005-migration-strategy-side-by-side-then-cutover)). Its model reproduces the legacy schema ([ADR-0010](#adr-0010-data-model)), and plan decision 5 makes EF Core code-first migrations the way to create and change it.
- The `dotnet-ef` tool has to build the `DbContext` at design time. By default it runs `Program.cs` up to `builder.Build()`, in the Development environment, and takes the context from the app's services. Every design-time command then depends on the app's service registrations and on the Development configuration, including its connection string ([ADR-0010](#adr-0010-data-model)).
- Migrations are generated code. The generated `InitialCreate.cs` breaks two of the repository's enforced rules: IDE0161 (file-scoped namespaces) and CA1861 (constant arrays as arguments). The designer and snapshot files already carry an `<auto-generated />` header.
- [ADR-0007](#adr-0007-test-strategy) set the direction for persistence tests: SQL Server in Testcontainers, one container per test assembly and one database per test class. The EF Core in-memory provider and SQLite cannot run the `catalog_hilo` sequence.

### Decision

**1. Migrations**

- The migrations live in `src/eShop.Catalog.Api/Data/Migrations`. The first one, `InitialCreate`, creates the schema of ADR-0010: the `catalog_hilo` sequence, the three tables with their EF6 names, and the brand and type reference data.
- They are generated by `dotnet-ef`, a local tool pinned in `dotnet-tools.json` to the version of the EF Core packages. The two are upgraded together; the tool warns when it is older than the runtime.
- The API references `Microsoft.EntityFrameworkCore.Design`, which the tool needs, with `PrivateAssets="all"` and without compile assets. The app's code cannot use it, and it stays out of the test projects and the published app.
- The generated files are committed as the tool writes them and reviewed like any other change. `.editorconfig` marks them as generated code, so the style and analyzer rules skip them; compiler warnings still apply. SQL that EF Core cannot generate goes into a migration's `Up` and `Down` by hand, with a comment that says why.
- `Down` is kept and tested: the migrations can be reverted until no catalog table or sequence is left, and applied again. EF Core's history table stays, empty.
- A unit test fails while the model has changes that no migration captures (`HasPendingModelChanges`).

**2. Design time**

`CatalogDbContextDesignTimeFactory` builds the context for the tool, with the app's SQL Server options (`UseCatalogSqlServer`) and no connection string. The tool never runs `Program.cs`, so design-time commands do not depend on the app's registrations or on any environment's configuration.

- Adding a migration and generating a script need no configuration and no database.
- `database update` takes the connection with `--connection`.
- `migrations remove` tries to check whether the migration is applied, cannot connect, and needs `--force`.

**3. Applying migrations**

| Where | How |
|---|---|
| Integration tests | `CatalogApiFactory` migrates each test class's database before its first test. |
| Development | By hand with `dotnet ef database update`, until Stage 4.4 adds config-gated migration at startup (Development only). |
| Deployed environments | An idempotent script (`dotnet ef migrations script --idempotent`), reviewed and applied by the deployment. A test applies the script twice to an empty database. Applying migrations is a deployment step, so the app's login needs no rights to change the schema. |

The script brings an empty database, or one that these migrations created, up to date. It cannot update a legacy database, which has the tables but no EF Core history: that needs the Stage 4.3 baseline first. A deployment creates the empty database itself, with `READ_COMMITTED_SNAPSHOT` on, as EF Core's database creator does for the test and Development databases.

**4. Test databases**

- `SqlServerFixture`, an xUnit assembly fixture, starts one SQL Server container for the integration test assembly.
- The image is pinned: `mcr.microsoft.com/mssql/server:2025-CU9-ubuntu-24.04`. SQL Server 2025 is the version the legacy capture ran on ([`capture-info.json`](docs/legacy/capture-info.json)). An upgrade is a deliberate change of this tag.
- Each `CatalogApiFactory`, and so each test class, gets a database of its own in that container, with a unique name, and migrates it in `InitializeAsync`. The factory takes the fixture as a constructor argument. xUnit v3 resolves it, although its documentation still says that fixtures cannot depend on other fixtures; the test runs are the evidence.
- A test that needs a database outside a host asks the fixture for one.
- Nothing drops the databases. Testcontainers removes the container at the end of the run.

This amends ADR-0007 in one point. ADR-0007 has persistence tests reach the database through the factory's services. Tests that inspect the schema connect to the class's database with SqlClient instead, and tests that need a database outside a host take one from the fixture.

**5. Schema verification**

The integration tests read the migrated database's schema from the `sys.*` catalog views, with queries modelled on the ones that captured `schema.json`, and compare it with that file through the schema facts of ADR-0010. For a live database, the facts also cover what EF Core cannot express but a hand-written migration could change:

- the update action of each foreign key, and whether it is enabled and trusted
- whether each sequence is cached
- the number of objects of each type in `sys.objects`, which catches table triggers, views, procedures, functions, synonyms and constraints, none of which have facts of their own
- schemas of their own, user-defined types and database-level DDL triggers, which `sys.objects` does not list and which must not exist

Users, permissions and statistics are not compared. The tests also check the brand and type rows, and that the history table is in `dbo` with the shape EF Core creates.

**Tests**

| Test | What it pins |
|---|---|
| `MigrationSnapshotTests.Model_has_no_changes_missing_from_the_migrations` (unit) | Every model change comes with a migration. |
| `MigrationTests.Every_migration_is_applied` | The factory's database has all the migrations. |
| `MigrationTests.Database_has_exactly_the_legacy_catalog_tables` | The migrations create `dbo.Catalog`, `dbo.CatalogBrand` and `dbo.CatalogType`, besides the history table, and nothing else. |
| `MigrationTests.Table_matches_the_legacy_schema` (one case per table) | The live tables equal `schema.json` under the comparison rules. |
| `MigrationTests.Only_sequence_is_the_legacy_item_id_sequence` | The live `catalog_hilo` equals `schema.json`, and there is no other sequence. |
| `MigrationTests.Database_has_no_objects_beyond_the_legacy_schema` | The object counts equal `schema.json`'s, less what the comparison leaves out, plus EF Core's history table. There are no schemas, user-defined types or database-level DDL triggers. |
| `MigrationTests.Migrations_history_table_is_in_dbo_as_ef_core_creates_it` | `__EFMigrationsHistory` is in `dbo`, with EF Core's columns and primary key. The test login's default schema is `dbo`, so this test cannot see the table being left in a login's default schema; `CatalogDbContextRegistrationTests.Migrations_history_table_is_in_dbo` (ADR-0010) covers that. |
| `MigrationTests.Brands_have_their_legacy_ids`, `Types_have_their_legacy_ids` | The seeded rows equal `seed-data.json`. |
| `MigrationRollbackTests.Migrations_revert_to_an_empty_database_and_apply_again` | `Down` removes the catalog tables and the sequence, and the migrations then apply again to the full legacy schema. |
| `MigrationScriptTests.Idempotent_script_creates_the_legacy_schema_and_can_run_again` | The deployment script, run twice on an empty database, creates the full legacy schema and records every migration. |

Each of these deliberate breaks failed the intended tests:

- a model change without a migration
- an index renamed in the migration
- a seeded brand or type changed in the migration
- a `Down` that leaves the sequence behind
- the history table put in another schema
- hand-written SQL in the migration that creates a table, a view, a table trigger, a schema, a user-defined type or a database-level DDL trigger, turns the sequence cache off, stops trusting a foreign key, or adds a column to the history table

### Alternatives considered

- **`EnsureCreated`.** It creates the schema without a migrations history, so the schema could never be changed with migrations afterwards.
- **Letting `dotnet-ef` run the app's host.** Every design-time command would run `Program.cs` up to `builder.Build()` in Development, and would depend on the app's service registrations and on the Development connection string. The factory depends on neither and builds the same context every time.
- **Migration bundles (`efbundle`) for deployment.** A bundle and a script both run at deploy time, with a connection that may change the schema. A script is plain SQL that can be reviewed before it runs; a bundle is an executable. Bundles can be revisited when a deployment pipeline exists.
- **Migrating at startup in every environment.** Every instance would migrate as it starts. EF Core serializes them with a lock, but startup would then wait on schema changes, and the app's login would need rights to change the schema.
- **SQL-first migrations (DbUp and similar).** They would lose the model diff and the check that the model and the migrations agree.
- **A database per test.** Creating and migrating a database takes longer than all the tests of a typical class. One database per class, as ADR-0007 planned, isolates classes. Tests in one class must not depend on each other's writes.
- **The `latest` image tag.** It moves without notice, so a run could pass or fail depending on when the image was pulled.

### Consequences

- The integration tests need Docker, including the ones that never query the database, because every factory migrates a database. The unit tests need only the SDK.
- The assembly fixture starts the container before the first test runs. Stage 10.2's Docker-free subset therefore needs a fixture that starts the container only when a test asks for a database, and a factory without one.
- The first run on a machine, and every CI run, pulls the SQL Server image.
- A model change without a migration fails the unit tests.
- Adopting an existing legacy database is a separate, tested procedure (Stage 4.3).

---

## ADR-0012: Adopting a legacy database

- **Status:** Accepted
- **Date:** 2026-09-29
- **Plan stage:** 4.3

### Context

- The new API gets a database of its own. Adopting an existing legacy database is a separate, tested procedure ([ADR-0005](#adr-0005-migration-strategy-side-by-side-then-cutover), [ADR-0011](#adr-0011-ef-core-migration-strategy)).
- A legacy database has the schema that `InitialCreate` creates ([ADR-0010](#adr-0010-data-model)). It also has EF6's `__MigrationHistory`, the unused `catalog_brand_hilo` and `catalog_type_hilo` sequences, and data. It has no `__EFMigrationsHistory`.
- EF Core decides what to apply only from `[dbo].[__EFMigrationsHistory]`. On a legacy database, `Migrate`, `dotnet ef database update` and the idempotent script all run `InitialCreate`, and fail on the existing legacy objects, the `catalog_hilo` sequence first. By then EF Core has already created an empty history table, outside the migration's transaction.
- Both apps take item IDs from `catalog_hilo` in blocks of 10. The legacy generator hard-codes the block size, and EF Core takes it from the model, so neither reads the sequence's increment ([audit](docs/legacy-audit.md#53-sequences-and-hilo)).
- A real legacy database may have drifted from the captured one:
  - indexes, statistics or triggers added by a DBA
  - sequences created by hand. The legacy sequence scripts hard-code the database name ([audit D12](docs/legacy-audit.md#7-defects-and-risks)), so a database with another name got its sequences some other way.
  - reference data from the CSV customization seed, which [ADR-0003](#adr-0003-non-goals) dropped
  - rows imported with explicit IDs
- EF Core 7 and later write with `OUTPUT` clauses without `INTO`, which SQL Server rejects on a table that has an enabled trigger.
- A design critique, run before the implementation, checked these facts by experiment, against the real `eShopLegacyMVC.dll` and EF Core 10.0.12:
  - EF6 6.2 starts without `__MigrationHistory`. When the table is there, EF6 compares the stored model with the code model, never with the live schema.
  - EF Core ignores the history row's `ProductVersion`, and matches the whole `MigrationId`, timestamp included.
  - A trigger, an increment other than 10, a restarted sequence, and an imported ID above the sequence all passed a simpler draft of the baseline, then broke EF Core writes or produced duplicate IDs.
- A review of the implementation found more by experiment:
  - A login without `VIEW DEFINITION` sees no rows in `sys.sql_expression_dependencies`, so a schema-bound view or a row-level-security policy on the catalog tables passed the checks.
  - With `IMPLICIT_TRANSACTIONS` on, the script reported success while its work stayed uncommitted.
  - Comparing indexes and foreign keys by name alone missed an index redefined under its legacy name and a disabled foreign key.

### Decision

**1. A baseline script that only adds**

[`docs/legacy/baseline.sql`](docs/legacy/baseline.sql) creates `[dbo].[__EFMigrationsHistory]` exactly as EF Core does and records `InitialCreate` as applied, with the `ProductVersion` of that migration. Nothing else changes: EF6's history table, the unused sequences and all data stay. Keeping them costs nothing and leaves nothing to undo. EF6 does not need its history table to start.

**2. Checks before any write**

The script refuses, with its own error number and a list of what differs, a database that:

| Error | Refused because |
|---|---|
| 50001 | the script runs inside a transaction, or with `IMPLICIT_TRANSACTIONS` on, either of which could still roll it back. The caller's transaction is left as it was. |
| 50002 | the server has no `sys.sequences.last_used_value` (older than SQL Server 2017) |
| 50012 | the login lacks `VIEW DEFINITION` on the database, so the checks could not see every object |
| 50003 | EF Core's migrations lock is held for more than 30 seconds |
| 50004 | `__EFMigrationsHistory` exists but is not EF Core's table |
| 50005 | `__EFMigrationsHistory` holds other migrations but not `InitialCreate` |
| 50006 | the catalog tables or `catalog_hilo` are missing |
| 50007 | the columns of the catalog tables differ from the legacy schema, in either direction: name, type, nullability, identity, a default or a computed expression, and a collation other than the database's |
| 50008 | the rest of the catalog tables differs from the legacy schema, in either direction. That covers keys and indexes by kind, clustering, columns, filter and state; user-created statistics; check and default constraints; triggers; foreign keys, the ones into the tables included, by columns, actions and state; and schema-bound objects on the tables. |
| 50009 | `catalog_hilo` is not a `bigint` sequence with `INCREMENT BY 10` and `NO CYCLE` |
| 50010 | an item ID is at or above the sequence's next value, or the sequence cannot hand out a whole next block of `int` item IDs |
| 50011 | the brands or types differ from the reference data, compared byte for byte in both directions |

The expected lists describe the legacy schema, which is the schema of `InitialCreate`. They never follow later migrations, because the baseline only ever stands in for `InitialCreate`.

**3. Refuse rather than warn**

After the baseline, the migrations own the schema, and they know only the legacy objects:

- An unknown index or statistic can stop a later column change.
- An enabled trigger breaks EF Core's writes.
- Extra reference rows would meet later `HasData` changes.

So the operator decides explicitly. An index can be dropped first and added back through a migration after the adoption. A customization database is out of scope ([ADR-0003](#adr-0003-non-goals)).

**4. Partial states and repeats**

- `InitialCreate` already recorded: nothing to do. This covers a second run and a database that the migrations created.
- An empty history table, left by a migration that failed on the legacy objects: adopted as usual.
- Other migrations without `InitialCreate`: refused (50005).

**5. Safe to run**

- One transaction with `XACT_ABORT`, so any failure leaves the database as it was.
- The script refuses to run inside a transaction or with implicit transactions (50001). That check runs before `XACT_ABORT` is set, so the refusal leaves the caller's own transaction open, for the caller to end.
- The script takes EF Core's migrations lock (`__EFMigrationsLock`), so `Migrate` and `dotnet ef database update` wait for it, and it waits for them. The idempotent migrations script takes no lock. What protects that path is the procedure's rule to run the baseline first.
- `LOCK_TIMEOUT` is 30 seconds.
- It holds exclusive locks only on the new history table and on system-catalog rows. On the catalog tables it takes brief shared locks while it reads, so it can run while the legacy app serves requests.

**6. One script for sqlcmd and SqlClient**

- One batch: no `GO`, no sqlcmd commands or variables, no double-quoted identifiers.
- Statements that read columns of the catalog tables run through `sp_executesql`. In the batch itself, a renamed column would stop the whole batch at compile time, before the column check could report it.
- The operator runs the script with `sqlcmd -b`, whose exit code tells adoption from refusal. The tests run the same file through SqlClient with `QUOTED_IDENTIFIER` off, as sqlcmd does, and through the container's own sqlcmd.

**7. Coexistence and rollback**

- The legacy app can keep running against an adopted database. Given checks 50009 and 50010, item IDs cannot collide: each app takes `NEXT VALUE` and uses that value and the next nine, and every existing ID lies below the next value.
- This amends [ADR-0005](#adr-0005-migration-strategy-side-by-side-then-cutover) decision 2 in one point: an adopted database is shared by the legacy app and the new API until Stage 11 retires the legacy app. A database that the migrations created is still the new API's own.
- Rolling back to the legacy app means pointing it at the same database. That holds while every later migration is expand-only, until Stage 11: no renamed or dropped columns that the legacy app maps, and new columns nullable or with a default. EF6's model check would not notice a contracting change; the legacy app would fail at runtime.
- `InitialCreate` must never be reverted on an adopted database: its `Down` drops the legacy tables and the sequence.
- Undoing the baseline before any later migration is `DROP TABLE dbo.__EFMigrationsHistory`.

**8. Procedure**

[docs/legacy/README.md](docs/legacy/README.md#adopting-an-existing-legacy-database) documents:

- the requirements. The server is SQL Server 2019 or later, the oldest version EF Core 10 supports, or Azure SQL; the script itself needs SQL Server 2017. The login has `db_owner`, or `db_ddladmin`, `db_datareader`, `db_datawriter` and `VIEW DEFINITION` on the database.
- a copy-only backup, and ideally a dry run on a restored copy
- running the baseline with `sqlcmd -b` before anything applies migrations, and checking the exit code
- the undo and rollback rules above

**9. Stage 4.4 constraint**

An adopted database must never receive the sample items. The Stage 4.4 seeder has to guarantee that, because `dotnet ef database update` and migrate-on-startup run EF Core's seeding on every call.

**Tests**

Integration tests (`LegacyBaselineTests`) build each legacy database the way the legacy app leaves it:

- the schema from `schema.sql`
- the rows from `seed-data.json`
- EF6's history row, with the gzip-compressed model from `ef6-model.edmx`
- every sequence drawn as far as the legacy seeding drew it
- `READ_COMMITTED_SNAPSHOT` on, as EF6 and EF Core create databases

| Test | What it pins |
|---|---|
| `Adopted_database_matches_a_migrated_one_apart_from_the_legacy_objects` | After the baseline and `Migrate`, every migration is recorded. The schema equals a migrated database's, apart from EF6's history table and the two unused sequences, which equal `schema.json`. The object counts equal the legacy counts plus EF Core's history table, and EF6's history row is unchanged. |
| `Adopted_database_takes_the_idempotent_migrations_script` | The deployment script records every migration and changes no schema. |
| `EF_Core_reads_and_writes_an_adopted_database_with_ids_after_the_legacy_ones` | EF Core reads the 12 legacy items, with their brands and types, equal to `seed-data.json`. A new item gets ID 21, the first ID of the block after the legacy seeding's. The legacy app's next block starts at 31. Updates and deletes work. |
| `Baseline_changes_nothing_when_it_runs_again`, `Baseline_changes_nothing_in_a_database_that_the_migrations_created` | The repeat cases. |
| `Baseline_adopts_a_database_that_a_failed_migration_left_behind` | The empty history table is accepted. |
| `Baseline_refuses_a_legacy_database_that_has_changed` (29 cases), `Baseline_refuses_a_database_that_is_not_a_legacy_one`, `Baseline_refuses_to_run_with_implicit_transactions`, `Baseline_refuses_a_login_without_VIEW_DEFINITION` | Each refusal reports its error number, leaves no transaction open, and changes nothing. The comparison covers the schema, the object counts, both history tables, the rows of the catalog tables, the sequences' positions and the identity values. The cases cover both directions of each list comparison, and the boundaries of 50010: a sequence restarted at the highest ID, an item imported at the sequence's next value, and a sequence at the end of the `int` range. |
| `Baseline_refuses_to_run_inside_a_transaction` | 50001 leaves the caller's transaction open and the database unchanged. |
| `Baseline_adopts_with_the_documented_rights_and_still_sees_schema_bound_objects` | A database user with exactly the documented roles and `VIEW DEFINITION` adopts a legacy database, and is refused one with a schema-bound view. |
| `Sqlcmd_exits_with_0_when_the_baseline_adopts_a_database_and_with_1_when_it_refuses` | The documented `sqlcmd -b` run, with the image's own sqlcmd. |

Unit tests (`LegacyBaselineScriptTests`) need no database:

- the script records the first migration's exact ID
- it creates the history table with EF Core's own DDL
- it has no `GO` separators, sqlcmd commands or variables, or double-quoted identifiers

Before the review, each check in the script was switched off in turn, and each time exactly the tests for that check failed. With the "already recorded" branch removed, the repeat tests failed. The conditions and cases added after the review have not all been proven against such breaks.

Not tested:

- 50002, because the test server is SQL Server 2025
- 50003, the lock wait
- running the legacy app itself against an adopted database. The design critique did that once, by hand.

### Alternatives considered

- **Copying the legacy data into a new database that the migrations create.** The schema would carry no legacy objects. But the data would have to move, with downtime, and the item-ID sequence would have to move with it. The legacy app could no longer be rolled back to by pointing it at the same database.
- **A conditional `InitialCreate`** that skips objects that already exist. Every database would run that condition forever, for a situation that only adoption has.
- **Letting the app adopt the database at startup.** Schema decisions would move into the app's startup, and the app's login would need rights to change the schema ([ADR-0011](#adr-0011-ef-core-migration-strategy)).
- **Dropping EF6's history table and the unused sequences in the baseline.** EF Core does not need them gone, and dropping them would make the baseline destructive, and harder to undo.
- **Warnings instead of refusals** for extra indexes and extra reference rows. The adoption would succeed, and a later migration or `HasData` change would then fail in production (decision 3).
- **Checking only that the legacy objects exist**, as the first draft did. The critique showed that this adopts databases on which EF Core then fails or produces duplicate IDs.

### Consequences

- Adopting a legacy database is one reviewed script, run with `sqlcmd -b`. A refusal names what differs.
- Until Stage 11, every migration must be expand-only, so that the legacy app can still be rolled back to on an adopted database. Migration reviews check this.
- Adopted databases keep EF6's history table and the two unused sequences. After Stage 11 a migration may drop them, guarded with `IF EXISTS`, because databases that the migrations created do not have them.
- `baseline.sql` is tied to `InitialCreate`. The unit tests fail if the migration's ID or EF Core's history DDL change.
- Stage 4.4 must keep the sample items out of adopted databases (decision 9).

---

## ADR-0013: Seeding, migrate-on-startup and readiness

- **Status:** Accepted
- **Date:** 2026-09-29
- **Plan stage:** 4.4

### Context

- The legacy app seeds a database once, when EF6's `CreateDatabaseIfNotExists` creates it on the first request that uses the context ([audit: seeding](docs/legacy-audit.md#54-seeding-modelsinfrastructurecatalogdbinitializercs32-43)). It adds the brands and types, then 12 sample items, whose IDs its HiLo generator takes from `catalog_hilo` in two blocks (1–10 and 11–20). The sequence is left at 11 ([`seed-data.json`](docs/legacy/seed-data.json)). The seeding is not atomic and never runs again, so a failure leaves a partly seeded database for good ([audit D13](docs/legacy-audit.md#7-defects-and-risks)).
- [ADR-0010](#adr-0010-data-model) made the brands and types reference data in the migrations. The sample items were left to this stage, because their IDs come from HiLo.
- EF Core calls a seeder registered with `UseSeeding` and `UseAsyncSeeding` at the end of every `Migrate`, `EnsureCreated` and `dotnet ef database update`, whether or not anything was applied. It calls it after a revert too (`Migrate("0")`), when the tables are gone. The tool migrates synchronously, and a synchronous `Migrate` with only an async seeder throws, so both are needed.
- In EF Core 10, `Migrate` takes EF Core's migrations lock, commits each migration as it applies it, and then calls the seeder, still under the lock ([`Migrator.cs`](https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore.Relational/Migrations/Internal/Migrator.cs) and [`MigrationCommandExecutor.cs`](https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore.Relational/Migrations/Internal/MigrationCommandExecutor.cs), release/10.0). A failing seeder therefore leaves the migrations applied. A value drawn from a sequence stays drawn when a transaction rolls back.
- An adopted legacy database must never receive the sample items ([ADR-0012](#adr-0012-adopting-a-legacy-database), decision 9). The baseline keeps EF6's `__MigrationHistory` and the unused `catalog_brand_hilo` and `catalog_type_hilo` in it, and the legacy seeding has drawn its `catalog_hilo`.
- [ADR-0011](#adr-0011-ef-core-migration-strategy) left Development migrations to `dotnet ef database update` until this stage. Deployed environments apply the reviewed idempotent script, so the app's login needs no rights to change the schema.
- The liveness endpoint runs no check ([ADR-0007](#adr-0007-test-strategy)). Readiness, which does check the database, was left to this stage.

### Decision

**1. The sample-item seeder**

`SampleItemSeeder` (`Data/`) adds the legacy app's 12 sample items, without IDs. HiLo numbers them in list order, so a new database gets IDs 1–12, and `catalog_hilo` is left at 11, as after the legacy seeding. The items are written with one `SaveChanges`, so the seeding is all or nothing. The seeder then stops tracking them, because the context belongs to the caller.

EF Core keeps its HiLo blocks in memory for the life of the process, keyed by server, database and sequence, even when the sequence is dropped and created again. After a revert and a new `Migrate` in the same process, the items would get IDs from a block of the old sequence (13–20, then 1–4), which the new sequence hands out again later. The review of this stage found that by experiment. The seeder therefore checks each ID as it adds the item, and throws at the first one out of order. That refuses the old block before anything is drawn from the new sequence, so the database stays unused, and a new process seeds it. The running app and the tool never meet this, because each migrates once per process; tests that revert and migrate again do.

It is registered in `UseCatalogSqlServer`, so the app, the tests and the `dotnet-ef` tool seed the same way. The synchronous and the asynchronous seeder share the item list and the conditions.

**2. When it seeds**

Only a database that nobody has used yet. Each of these conditions stops it on its own:

| The seeder does nothing when | Because |
|---|---|
| a migration is not applied | It writes through the current model. This also stops it after a revert. |
| the catalog has an item | |
| `catalog_hilo` has handed out a value (`sys.sequences.last_used_value`) | A database is seeded once, as `CreateDatabaseIfNotExists` did. An emptied catalog is not refilled. The legacy seeding has drawn the sequence of every legacy database. |
| `dbo.__MigrationHistory`, `dbo.catalog_brand_hilo` or `dbo.catalog_type_hilo` exists | Only a legacy database has them, and the baseline keeps them. |

So seeding again changes nothing, and an adopted database keeps its data even when its catalog is empty.

If the seeding fails after it has drawn IDs, for example because `SaveChanges` loses its connection, no item is written, but the IDs stay drawn, and EF Core has committed the migrations already. The sequence has then been used, so no later `Migrate` seeds that database. For a Development database the remedy is to drop it.

**3. Where the sample items appear**

Only where EF Core migrates:

- in Development, at startup (decision 4)
- with `dotnet ef database update`, in any database that `--connection` names. The tool has no environment ([ADR-0011](#adr-0011-ef-core-migration-strategy), decision 2).
- in the tests: every database that `CatalogApiFactory` or `CatalogDatabase.CreateMigratedAsync` migrates holds them, as the database of the Stage 1.2 capture did. The Stage 7 comparisons with the golden exchanges rely on this.

The idempotent script, with which deployments apply the migrations, holds only the reference data. A new deployed database therefore starts without the sample items, where the legacy app seeded them into every new database. This is not a contract delta ([ADR-0002](#adr-0002-wire-contract-policy)): the contract covers routes, verbs, status codes and response shapes, not the rows of a new database, and a database adopted at cutover keeps its data. Nor is it a business-rule change: it concerns how a database is provisioned, not a rule of an endpoint, so [docs/behavior-changes.md](docs/behavior-changes.md) gets no entry.

**4. Migrate on startup**

- `Database:MigrateOnStartup`, bound to `DatabaseOptions` ([ADR-0009](#adr-0009-configuration)). It is `false` in `appsettings.json` and `true` in `appsettings.Development.json`.
- `MigrateOnStartupService`, an `IHostedLifecycleService`, migrates in `StartingAsync`. The host calls that before it starts any hosted service, the server included, so no request meets an old schema. Stopping the host during startup cancels the migration: EF Core rolls back the migration it is applying, and keeps the ones it has committed.
- Outside Development, the host refuses to start with the setting on: options validation fails at startup and names the setting. A stray `Database__MigrateOnStartup` cannot migrate a deployed database, or seed sample items into it. A malformed value stops the host too.
- The environment rule is a `Validate` delegate on the options builder, not a data annotation as ADR-0009 decision 3 has it, because it needs `IHostEnvironment`. `ValidateOnStart` applies it with the rest.
- The integration tests run in the `Testing` environment and keep migrating their databases in `CatalogApiFactory`. `MigrateOnStartupTests` start Development hosts on purpose, from factories that xUnit never initializes. Those hosts read `appsettings.Development.json` and the developer's user secrets, so the tests pass their settings as host settings, which override both. This amends a consequence of ADR-0009: a developer's user secrets reach these test hosts, but cannot change the settings that the tests depend on.

This amends ADR-0011 decision 3: a Development database is migrated at startup. `dotnet ef database update` still works, and seeds the same way.

**5. Readiness**

`GET /health/ready` runs the checks tagged `ready`. The only one, `catalog-database`, passes when the API can connect to the database and the database has every migration that this build knows. It answers `200 Healthy` or `503 Unhealthy`, as `text/plain`, with the status only. The reason, such as the missing migrations, goes to the log. Liveness does not change: a database outage takes an instance out of rotation, but does not get it restarted.

The check has no timeout of its own. A server that does not answer holds the check for SqlClient's connect timeout, 15 seconds by default, so the probe's own timeout usually ends it first. A deployment target may call for a shorter one.

**Tests**

Integration tests:

| Test | What it pins |
|---|---|
| `SampleItemSeedingTests.Migrations_seed_the_legacy_sample_items_with_their_legacy_ids` | A migrated database holds exactly the item rows of `seed-data.json`, IDs 1–12 included. |
| `SampleItemSeedingTests.Seeded_items_point_at_their_legacy_brands_and_types` | Through the navigations, each item has the brand and type names that `seed-data.json` gives it. |
| `SampleItemSeedingTests.Item_id_sequence_stands_where_the_legacy_seeding_left_it` | `catalog_hilo` is at 11. |
| `SampleItemSeedingTests.Migrating_again_seeds_nothing` | Two more `MigrateAsync` calls change neither the rows nor the sequence. |
| `SampleItemSeedingTests.Synchronous_migration_seeds_the_same_items_once` | The synchronous seeder, the tool's, seeds the same 12 items once, over two `Migrate` calls, leaves the sequence at 11, and leaves nothing tracked on the context. |
| `SampleItemSeedingTests.Items_added_after_seeding_take_ids_above_the_seeded_ones` | No HiLo collision: the next item in the same process gets 13, the rest of the seeding's block, as in the legacy app. Another process's next block starts at 21. |
| `SampleItemSeedingTests.Seeding_refuses_hilo_ids_from_before_the_sequence_and_leaves_the_database_unused` | The asynchronous seeder leaves nothing tracked. After a revert, a `Migrate` in the same process fails in the seeder, again with nothing tracked. The migrations stay applied, the catalog empty and `catalog_hilo` unused. A new process then seeds IDs 1–12. |
| `SampleItemSeedingTests.Migrations_seed_nothing_into_an_adopted_legacy_database` (6 cases) | An adopted database, as the baseline leaves it and stripped of all but one sign of use (items, a used `catalog_hilo`, EF6's history table, either unused sequence), keeps its rows and its sequence through `Migrate`. |
| `MigrationRollbackTests.Migrations_revert_to_an_empty_database_and_apply_again` (amended) | The migrations, applied again as by a new process, seed the items with IDs 1–12 again. |
| `MigrateOnStartupTests.Development_host_migrates_and_seeds_its_database_before_the_server_starts` | A Development host with the setting on creates, migrates and seeds its database, and is ready. A hosted service registered after the app's own finds every migration applied in its `StartingAsync`, which the host calls before it starts the server. |
| `MigrateOnStartupTests.Host_leaves_the_database_alone_with_migrate_on_startup_off` (3 environments) | With the setting off (`false` in Development, the `appsettings.json` default in Testing and Production), the host starts and never creates its database. |
| `MigrateOnStartupTests.Host_outside_Development_refuses_to_start_with_migrate_on_startup` (Testing, Staging, Production) | The host fails at startup with a message that names the setting, and the database is not created. |
| `MigrateOnStartupTests.Host_does_not_start_with_a_malformed_migrate_on_startup` | `yes` stops the host, with a message that names the setting. |
| `ReadinessEndpointTests` | 200 `Healthy` on a migrated database; 503 `Unhealthy`, with nothing but the status, on a database that does not exist and on one without the migrations. Liveness stays 200 in both. |
| `ConfigurationTests.Committed_settings_turn_migrate_on_startup_on_in_Development_only` | The setting is off in `appsettings.json` and on in `appsettings.Development.json`. It reads only the committed files, so a developer's user secrets cannot fail it. |

Unit tests:

| Test | What it pins |
|---|---|
| `SampleItemSeederTests.Sample_items_are_the_legacy_ones_in_id_order` | Numbered in list order, the seeder's items equal `seed-data.json`. |
| `ReadinessPolicyTests.Readiness_runs_only_the_checks_tagged_ready` | Readiness ignores untagged checks. |

Apart from the rollback test, no existing assertion changed, but every database that the migrations create now holds the sample items.

Each of these deliberate breaks failed the intended test:

- the seeder without each of its conditions in turn: migrations applied, no item, an unused sequence, and each of the three legacy objects
- the items saved with explicit IDs instead of HiLo
- the seeder without its check of the IDs
- the items left tracked after seeding
- the synchronous seeder saving nothing
- a sample item's price changed
- migrate-on-startup allowed in every environment, or run whatever the setting
- the migration moved from `StartingAsync` to `StartAsync`
- readiness ignoring missing migrations, or running every check
- the setting turned off in `appsettings.Development.json`

Not tested:

- that stopping the host during startup cancels the migration
- two instances migrating at once, which EF Core's migrations lock serializes

A Development host was also run by hand against LocalDB: it applied `InitialCreate` and seeded items 1–12 before it logged `Now listening`, and `/health/ready` answered 200. `dotnet ef database update`, run twice on a new LocalDB database, seeded the 12 items once.

### Alternatives considered

- **Sample items in `HasData`.** The items would need fixed IDs, which HiLo never draws, so the first item created afterwards would get ID 1 and collide (the explicit-ID break shows it). They would also be in the idempotent script, and so in every deployed database.
- **A migration that inserts the items with `NEXT VALUE FOR`.** An adopted database would run it after the baseline, unless the SQL guarded against that, and every deployed database would get demo data.
- **Seeding in `Program.cs` or in a hosted service of its own.** EF Core's hook runs under the migrations lock, and in `dotnet ef database update` as well.
- **Seeding whenever the catalog is empty**, as the EF Core documentation shows. It would retry a seeding that failed after drawing IDs. But an emptied Development catalog would fill again at the next start, and only the three legacy objects would keep the items out of an adopted database.
- **Recognizing an adopted database by a mark that the baseline writes**, such as a distinct `ProductVersion` in its history row. The baseline writes EF Core's own row on purpose (ADR-0012), and the mark would rely on EF Core ignoring that column.
- **Migrating at startup in every environment.** [ADR-0011](#adr-0011-ef-core-migration-strategy) rules it out.
- **Ignoring the setting outside Development** instead of refusing to start. A misconfigured deployment would look fine.
- **`AddDbContextCheck`** from `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore`. It needs one more package, and by default it only checks that the database can be reached, so a database without the migrations would look ready.

### Consequences

- `dotnet run` in Development creates, migrates and seeds the database, and needs the database server to be up. A new database is seeded once; to start over with the sample items, drop it.
- Every test database that the migrations create starts with the 12 sample items. A test that needs an empty catalog deletes them.
- Deployed databases start with the brands and types only.
- A deployment's readiness fails until its migrations are applied, so the script runs before the new build is expected to be ready.
- The seeder recognizes a legacy database by objects that a migration after Stage 11 may drop ([ADR-0012](#adr-0012-adopting-a-legacy-database)). A used `catalog_hilo` still keeps the items out, but that migration has to revisit this rule.
- A test that reverts the migrations and applies them again in the same process has to apply them from a context with EF Core's internal services to itself (`CatalogDatabase.CreateContextAsInANewProcess`).
- Stage 5.3's mock mode needs no database. It has to turn migrate-on-startup and the readiness database check off, or a Development host started without a database fails at startup.
- The two new unit tests need only the SDK. The other new tests need SQL Server.

---

## ADR-0014: Local development databases

- **Status:** Accepted
- **Date:** 2026-09-29
- **Plan stage:** 4.5

### Context

- The Development connection string points at LocalDB, database `eShopCatalog` ([ADR-0009](#adr-0009-configuration), decision 2). LocalDB runs only on Windows. ADR-0009 left the other operating systems to this stage: SQL Server in a container, reached through a connection string in user secrets.
- Since Stage 4.4 a Development host creates, migrates and seeds its database before it accepts requests, and does not start while the server is down ([ADR-0013](#adr-0013-seeding-migrate-on-startup-and-readiness), decision 4). Its login therefore has to be able to create a database and change its schema.
- The integration tests run the pinned image `mcr.microsoft.com/mssql/server:2025-CU9-ubuntu-24.04` ([ADR-0011](#adr-0011-ef-core-migration-strategy), decision 4). The LocalDB of the legacy capture is SQL Server 2025 as well (17.0.4025.3, [`capture-info.json`](docs/legacy/capture-info.json)).
- The image needs a password for the `sa` login: at least 8 characters, from three of four character groups. It uses the password only when it creates its system databases. Its certificate is self-signed, and SqlClient encrypts by default and validates the server's certificate.
- No committed file holds a credential ([ADR-0009](#adr-0009-configuration), decision 1). `.gitignore` already ignores `.env`.

### Decision

**1. Two options, LocalDB by default**

| | LocalDB | SQL Server in a container |
|---|---|---|
| Where | Windows | Any OS with Docker |
| Chosen by | `appsettings.Development.json`, unchanged | A connection string in user secrets, which overrides it |
| Server | The installed SQL Server Express LocalDB | `compose.yaml`, with the tests' image |
| Login | The developer's Windows account | `sa` |

Either way the app creates, migrates and seeds `eShopCatalog` as ADR-0013 describes. The README's [Local database](README.md#local-database) section has the steps.

**2. `compose.yaml` runs SQL Server only**

The file at the repository root has one service, `sqlserver`. The API is not in it: it runs with `dotnet run` on every OS, so debugging and hot reload work the same everywhere. The compose project is named `eshop-catalog`, so its volume has the same name in every clone, whatever the folder is called.

**3. The tests' image**

- The service runs the image of the integration tests. The image name moves from the constant `SqlServerFixture.Image` to `SqlServerImage.Name` in `tests/Shared/SqlServerImage.cs`, which both test projects compile, and a unit test keeps `compose.yaml` equal to it. So an upgrade changes both, and a developer's server is the server the tests ran on.
- There is no Arm64 image. `platform: linux/amd64` lets Docker Desktop on a Mac with Apple silicon pull the amd64 image and run it under Rosetta emulation. Other Arm64 hosts may not run it at all.
- The edition is Developer, the image's default, stated in the file: free, and not licensed for production.

**4. Credentials**

- The `sa` password comes from the variable `MSSQL_SA_PASSWORD`, which compose reads from the environment or from `.env` next to `compose.yaml`. With `${MSSQL_SA_PASSWORD:?…}`, every compose command stops with a message while it is unset or empty, instead of starting a server without a password. Nothing in the repository holds it.
- The connection string, with the password, goes into user secrets, as ADR-0009 planned. The password is therefore kept in two places on the developer's machine, both outside the repository.
- The app connects as `sa`. It has to create the database and change its schema at startup, and the image has no hook that could create a narrower login when it first starts.

**5. Network and encryption**

- The port is published on `127.0.0.1` only, so the `sa` login cannot be reached from another machine. The host port is 1433, and `CATALOG_DB_PORT` changes it.
- The documented connection string names `127.0.0.1` and keeps SqlClient's default encryption, with `TrustServerCertificate=True` for the self-signed certificate, as the Testcontainers connection strings of the tests do. The traffic is encrypted, but the server is not authenticated, which is acceptable only on the loopback interface.

**6. Data and health**

- The databases live in the named volume `eshop-catalog_sqlserver-data`, so they survive `docker compose down`. `docker compose down --volumes` starts over, which is also the simplest way to change the `sa` password. A named volume, not a folder in the working tree, keeps database files out of the repository and needs no host-folder permissions for the container's non-root `mssql` user.
- A health check logs in with the image's `sqlcmd`, so `docker compose up --wait` returns once the server accepts logins, and an app started after it finds the server ready. `sqlcmd` takes the password from `SQLCMDPASSWORD`, not from its command line, which other users of a Linux host could list. The check uses the password that compose gives the container now, so a password changed after the volume was created shows as an unhealthy container, not only as a login failure in the app.

**Tests** (`tests/eShop.Catalog.Api.UnitTests/Data/ComposeFileTests.cs`)

They read `compose.yaml` as text, one setting per line, because `docker compose config` would need Docker and the unit tests need only the SDK.

| Test | What it pins |
|---|---|
| `Compose_runs_the_sql_server_image_of_the_integration_tests` | `compose.yaml` names one image, `SqlServerImage.Name`. |
| `Compose_takes_the_sa_password_from_the_environment` | The password is a required variable without a default, and the file does not set `SA_PASSWORD`, the older name that the image still reads. |
| `Compose_publishes_sql_server_on_loopback_only` | Every published port is bound to `127.0.0.1`. A `ports` value on the key's own line, such as a flow list, fails the test instead of being skipped. |

A comment at the end of a line becomes part of the value, so the tests fail on it rather than pass.

Each of these deliberate breaks failed the intended test: another image tag, a password in the file, a password variable with a default (`:-`), a password under `SA_PASSWORD`, the port published on every interface, and the ports written as a flow list.

The compose file was also run by hand, with Docker Compose 5.0.2 on Windows:

- Without the password, `docker compose config` stopped with the message. With it, and with `.env` files that have CRLF line endings or a UTF-8 byte order mark, the password and `CATALOG_DB_PORT` came through.
- `docker compose up --detach --wait` returned in about 10 seconds with the server healthy. A Development host with the documented connection string created, migrated and seeded `eShopCatalog` (items 1–12, `catalog_hilo` at 11) before it logged `Now listening`, and `/health/ready` answered 200. `sys.dm_exec_connections` showed its connections encrypted. The connection string was passed as an environment variable, which overrides user secrets, so no developer's secrets were touched.
- The databases survived `down` and `up`. With another password, `up --wait` recreated the container and reported it unhealthy. A password that fails the policy made the container exit.
- After the health check moved to `SQLCMDPASSWORD`, `up --wait` again reported the server healthy.

Not checked: Linux and macOS hosts, and emulation on Arm64.

### Alternatives considered

- **The container as the default**, with its connection string in `appsettings.Development.json`. The file would hold a password, and every developer would need Docker, where on Windows LocalDB needs nothing and is what the legacy app uses.
- **The API in `compose.yaml` too.** It would need a Dockerfile and a second way to run and debug the API. An image of the API belongs with a deployment target, and there is none yet.
- **Aspire** (formerly .NET Aspire), an AppHost that starts SQL Server and the API. It adds two projects and another orchestration model to a solution with one service. Compose needs only Docker, which the integration tests need already.
- **A login other than `sa`.** Creating it needs a script after the first start, which the image has no hook for, and the login would need rights to create databases anyway.
- **`Encrypt=False`** instead of trusting the certificate. The session would not be encrypted, and the app would connect differently from the tests.
- **Publishing on every interface** (`1433:1433`, as most examples do). The `sa` login would be open to the network.
- **A folder in the working tree for the data.** Database files next to the code, and a folder that the container's `mssql` user must be able to write.

### Consequences

- The API can be developed on Linux and macOS, given Docker.
- The container option asks for the password twice, in `.env` and in the connection string. Changing it later means starting over with a new volume, or changing it on the server as well.
- Every clone and worktree on a machine shares the compose project `eshop-catalog`, and with it one container and one volume. `docker compose down --volumes` in any of them removes the databases of all of them, and a clone with another password in its `.env` recreates the shared container, which then reports unhealthy. A second clone that needs its own server runs compose with another project name (`--project-name`) and port.
- An upgrade of the tests' image upgrades the local server too. A later major version upgrades the databases in the volume when it starts, and an older server cannot open them afterwards; `docker compose down --volumes` starts over.
- No automated test runs `compose.yaml`: its image, password variable and port binding are tested, and the run above is the evidence that the whole works.

---

## ADR-0015: Async-first catalog service

- **Status:** Accepted
- **Date:** 2026-09-30
- **Plan stage:** 5.1

### Context

- The legacy controllers reach the data through `ICatalogService` ([audit: components](docs/legacy-audit.md#21-components)). It has seven synchronous operations and is `IDisposable`. `CatalogService` implements it over EF6, and `CatalogServiceMock` over an in-memory list.
- The audit traces these defects to the service ([audit: defects](docs/legacy-audit.md#7-defects-and-risks)):
  - **D2.** Edit attaches the posted object with `EntityState.Modified`, so every column is written, fields the form does not post included. `Id` and `PictureFileName` are bindable ([`edit-overwrites-unposted-fields`](docs/legacy/evidence/edit-overwrites-unposted-fields.json)).
  - **D6.** Paging is not validated. `pageSize=0` divides by zero, negative values reach `Skip`/`Take`, and `pageSize * pageIndex` overflows ([`catalog-reads`](docs/legacy/evidence/catalog-reads.json)).
  - **D11.** An edit of an unknown ID throws `DbUpdateConcurrencyException`, and a delete passes `null` to `Remove` ([`unknown-item-writes`](docs/legacy/evidence/unknown-item-writes.json)).
  - **D16.** The controller disposes the service, and with it the `DbContext` that Autofac also disposes.
  - **D17.** `GET /api/brands/{id}` loads every brand and searches them in memory. `GET /api/brands` returns the deferred `DbSet`, so the query runs while the response is serialized.
  - **D15**, the mock, is Stage 5.2's.
- The legacy create replaces a posted ID with the next HiLo value ([`create-ignores-posted-id`](docs/legacy/evidence/create-ignores-posted-id.json)), and a new item gets `dummy.png` from the `CatalogItem` constructor ([`create-default-picture`](docs/legacy/evidence/create-default-picture.json)). EF Core generates a key only when the property holds its CLR default, so an entity with a posted ID would be inserted under that ID.
- Plan decision 6: the port is asynchronous from the start, and the async analyzers CA2016, CA1849 and CA2012 are errors ([ADR-0006](#adr-0006-solution-structure-and-build-conventions)). Stage 8 verifies that the request's `RequestAborted` token reaches EF Core.
- [ADR-0010](#adr-0010-data-model) left the `dummy.png` default to item creation. [ADR-0007](#adr-0007-test-strategy) planned a service contract suite, run against SQL Server.

### Decision

**1. The interface** (`Catalog/ICatalogService.cs`)

| Legacy | New | What changes |
|---|---|---|
| `GetCatalogItemsPaginated(pageSize, pageIndex)` | `GetCatalogItemsPaginatedAsync(pageSize, pageIndex, ct)` → `PaginatedItems<CatalogItem>` | A `pageSize` below 1 or a `pageIndex` below 0 throws `ArgumentOutOfRangeException` before any query. A page after the last one is empty. |
| `FindCatalogItem(id)` | `FindCatalogItemAsync(id, ct)` → `CatalogItem?` | |
| `GetCatalogBrands()`, a deferred `IEnumerable` | `GetCatalogBrandsAsync(ct)` → `IReadOnlyList<CatalogBrand>` | Read before it returns, in ID order (D17). |
| none | `FindCatalogBrandAsync(id, ct)` → `CatalogBrand?` | One brand, looked up by the store (D17). |
| `GetCatalogTypes()` | `GetCatalogTypesAsync(ct)` → `IReadOnlyList<CatalogType>` | Read before it returns, in ID order. |
| `CreateCatalogItem(CatalogItem)` | `CreateCatalogItemAsync(CatalogItemFields, ct)` → the new item | The input has no ID and no picture (decision 2). |
| `UpdateCatalogItem(CatalogItem)` | `UpdateCatalogItemAsync(id, CatalogItemFields, ct)` → `bool` | Writes exactly the given fields (decision 2). `false` for an unknown ID (D11). |
| `RemoveCatalogItem(CatalogItem)` | `RemoveCatalogItemAsync(id, ct)` → `bool` | By ID. `false` for an unknown ID (D11). |
| `Dispose()` | none | The container owns the context (D16). |

- **Asynchronous, with a required token.** Every operation returns a `Task` and takes a `CancellationToken` with no default value, so a caller cannot forget to pass the request's token. A token that is already cancelled stops every operation before it changes anything.
- **`Task`, not `ValueTask`.** Every call of the EF Core implementation does I/O, so a `ValueTask` would save nothing, and it has rules of use (CA2012).
- **Results belong to the caller.** A caller can change an item, brand or type that the service returned without changing the catalog. EF6's tracked queries let a later `SaveChanges` write such a change back, and the legacy mock handed out its own objects.
- **Not found is a value**: a `null` or `false`, not an exception. The endpoints decide the status: 404 for the brands and the items that are read (Stages 7.2 and 7.5), and 404 is also expected for updates and deletes of unknown items (Stage 7.7, [upcoming deltas](docs/behavior-changes.md#known-upcoming-deltas)).
- **Unknown brands and types are refused** on create and update, as the database's foreign keys refuse them, and nothing changes. The exception is not part of the contract: the EF Core implementation throws `DbUpdateException` on create and the `SqlException` of the foreign key on update. The endpoints are expected to check the brand and the type before they call the service, and answer 400. Stages 7.6 and 7.7 decide it ([upcoming deltas](docs/behavior-changes.md#known-upcoming-deltas)). An update of an unknown ID returns `false` whatever the fields hold, because the database's `UPDATE` then matches no row and checks no foreign key.

**2. Writes apply explicit fields** (`Catalog/CatalogItemFields.cs`)

`CatalogItemFields` holds the nine fields that a caller writes: `Name`, `Description`, `Price`, `CatalogTypeId`, `CatalogBrandId`, `AvailableStock`, `RestockThreshold`, `MaxStockThreshold` and `OnReorder`. Each is `required`, so a caller sets every one.

- **No ID.** The service assigns it, as the legacy create did.
- **No picture.** A new item gets `dummy.png` (`CatalogItem.DefaultPictureFileName`), and an update keeps the picture. This moves the default that ADR-0010 left to Stage 7.6 into the service, because every implementation has to apply it and the contract suite checks it. The constant is declared on `CatalogItem`, but the entity does not apply it. This amends ADR-0010 decision 1. Stage 7.6 keeps the HTTP half: its request contract has no picture.
- **An update writes the nine fields and nothing else.** That fixes D2 in the service: the ID and the picture cannot be overwritten, and a `null` description is written as `null` because the caller gave it. Which of the nine a request must carry is Stage 7.7's decision. `OnReorder` is among them, although the legacy edit form had no such field and so reset it on every edit.

**3. The EF Core implementation** (`Catalog/CatalogService.cs`)

- **Scoped**, on the scoped `CatalogDbContext`. Stage 5.3 registers it. Until then the tests construct it.
- **Reads** use `AsNoTracking`. Lists are ordered by ID: the legacy brand and type queries had no `ORDER BY`, so SQL Server chose the order. The golden brand exchanges show ID order, and types follow the same rule. Items come with their brand and type (`Include`), as in the legacy service.
- **Paging** counts with `LongCountAsync` and reads only the page, with `OFFSET`/`FETCH`, as the legacy service did. The offset is a `long`. A page that starts after the last item is empty without the page query, so an offset that reaches `Skip` is below the count. It fits an `int` unless the table holds more than `int.MaxValue` rows, and then a checked cast throws.
- **The brand lookup** is one query on the key (D17).
- **Create** adds the item with `AddAsync`, because HiLo may draw a new block from `catalog_hilo`, which is I/O, and then saves it. The item is detached afterwards, whether the save succeeded or not, so no later `SaveChanges` on the context writes it again.
- **Update** is one `ExecuteUpdateAsync` that sets the nine fields. **Delete** is one `ExecuteDeleteAsync`. Each returns `true` when it touched a row.
  - Neither reads the row first. A row that another request deletes in the meantime gives `false`, not a concurrency exception (D11).
  - Both bypass the change tracker, which holds nothing of the service's that could go stale.
  - Each write is one statement or one `SaveChanges`, so no explicit transaction is needed.

**4. `PaginatedItems<T>`** (`Catalog/PaginatedItems.cs`)

It replaces `PaginatedItemsViewModel` and keeps its property names (`ActualPage`, `ItemsPerPage`, `TotalItems`, `TotalPages`, `Data`), which the items endpoint of Stage 7.5 is expected to serialize.

- **Guards** (D6): a `pageIndex` of 0 or more, a `pageSize` of 1 or more, a count of 0 or more, and a page that is not `null` and holds no more than `pageSize` items. The constructor does not check the page against the count. They come from two reads, which a concurrent write can put out of step, and that must not become an error.
- **`TotalPages`** is `ceil(count / pageSize)`, 0 for an empty list, as in the legacy class. It is a `long`, like `TotalItems`, computed with integer arithmetic instead of the legacy `decimal` division and `int` cast.
- **No upper bound on `pageSize`.** The limit of 100 is a rule of the HTTP API (Stage 7.5), not of the service.

**5. The contract suite**

`CatalogServiceContractTests` (`tests/Shared/Catalog`) is an abstract test class that both test projects compile. Each implementation's test class derives from it and supplies `Service`, over a catalog of its own that holds the legacy sample data: the brands, types and 12 items of [`seed-data.json`](docs/legacy/seed-data.json).

- The EF Core class, `CatalogServiceTests` in the integration tests, uses a context of the host on the class's migrated database ([ADR-0013](#adr-0013-seeding-migrate-on-startup-and-readiness)). Each test runs in a transaction that it never commits, so every test starts from the sample data. The two tests that inspect the SQL build a context of their own, with a command interceptor, outside the host and the transaction. They only read.
- The in-memory class arrives with Stage 5.2, in the unit tests, so it needs no Docker.

**Tests**

The contract, run against the EF Core implementation:

| Test | What it pins |
|---|---|
| `Brands_are_the_legacy_brands_in_id_order`, `Types_are_the_legacy_types_in_id_order` | The lists equal `seed-data.json`, in ID order. |
| `Each_brand_is_found_by_its_id`, `Unknown_brand_is_not_found` (0, −1, 6, `int.MaxValue`) | The brand lookup. |
| `Page_holds_the_items_at_its_place_in_id_order` (8 cases) | The items on the page, `ActualPage`, `ItemsPerPage`, `TotalItems` and `TotalPages`: first, last, exact and one-item pages, a page after the last one, and a `pageIndex` whose offset overflows an `int`. |
| `Paged_items_come_with_their_brands_and_types` | Each item carries the names of its brand and type. |
| `Invalid_paging_is_refused` (5 cases) | `ArgumentOutOfRangeException` naming `pageSize` or `pageIndex`. |
| `Each_item_is_found_by_its_id_with_its_brand_and_type`, `Unknown_item_is_not_found` (0, −1, 13, `int.MaxValue`) | The item lookup, every column included. |
| `Created_item_gets_a_new_id_the_given_fields_and_the_default_picture` | A new ID, every given field, `dummy.png`, no brand or type loaded, and the same item when read back. |
| `Ids_are_never_given_again` | After a created item and item 12 are removed, the next item gets neither ID. |
| `Update_writes_every_field_and_keeps_the_id_and_the_picture` | Every field changes, a `null` description included. The ID, the picture and every other item do not. |
| `Update_of_an_unknown_item_changes_nothing`, `Removing_an_unknown_item_changes_nothing` (0, 13, `int.MaxValue`) | `false`, and the catalog is unchanged. |
| `Update_of_an_unknown_item_with_an_unknown_brand_returns_false` | The unknown ID wins over the unknown brand and type. |
| `Removed_item_is_gone` | The item is gone from lookups and pages, and a second removal returns `false`. |
| `Pages_stay_in_id_order_after_removes_and_creates` | After item 1 is removed and an item created, the page holds items 2–12 and then the new one. |
| `Item_with_an_unknown_brand_or_type_is_refused` (4 cases) | Create and update throw, and the catalog is unchanged. |
| `Cancelled_operations_change_nothing` | With a token that is already cancelled, every operation throws `OperationCanceledException`, and the catalog is unchanged. |
| `Changing_what_the_service_returned_changes_nothing` | Changes to returned items, brands and types, followed by another create, leave the catalog as it was. |

Only the EF Core implementation:

| Test | What it pins |
|---|---|
| `CatalogServiceTests.Service_leaves_nothing_tracked` | After one call of each operation, a refused create and a refused update included, the context tracks nothing. The refused create throws `DbUpdateException`, and the refused update `SqlException`. |
| `CatalogServiceTests.Brand_lookup_asks_the_database_for_the_one_brand` | One `SELECT TOP(1) ... WHERE` (D17). |
| `CatalogServiceTests.Paging_reads_only_the_rows_of_the_page_in_id_order` | A `COUNT_BIG(*)`, then one query with `ORDER BY [c].[Id]`, `OFFSET` and `FETCH NEXT`. |
| `CatalogServiceTests.Brands_and_types_are_read_in_id_order` | Each list query has `ORDER BY [c].[Id]`. SQL Server returns these small tables in key order without one too, so the rows alone cannot show it. |

Unit tests:

| Test | What it pins |
|---|---|
| `PaginatedItemsTests.Page_keeps_what_it_was_given` | The four values and the page. |
| `PaginatedItemsTests.Total_pages_hold_every_item` (7 cases) | `ceil(count / pageSize)`, from an empty list up to `long.MaxValue` items. |
| `PaginatedItemsTests.Page_index_below_zero_is_refused` (2 cases), `Page_size_below_one_is_refused` (3 cases), `Negative_count_is_refused`, `Missing_data_is_refused`, `Page_larger_than_its_size_is_refused` | Each guard, with the parameter it names. |
| `PaginatedItemsTests.Page_may_disagree_with_the_count` | A page with more items than the count is accepted. |

`SampleItemSeedingTests` now takes the items' brand and type names from `LegacySeedData`, which the contract suite shares.

Each of these deliberate breaks failed the intended tests:

- the created item left tracked, or item reads that track: `Changing_what_the_service_returned_changes_nothing` and `Service_leaves_nothing_tracked`
- an update that also resets the picture, or that skips the description: `Update_writes_every_field_and_keeps_the_id_and_the_picture`
- a create that skips the description: the create, order and snapshot tests
- the item lookup without its brand and type: the lookup, create, update and snapshot tests
- the brand looked up among every brand, in memory: `Brand_lookup_asks_the_database_for_the_one_brand`, and `Cancelled_operations_change_nothing`, because that search ignored the token
- the offset computed as an `int`: the `int.MaxValue` case of `Page_holds_the_items_at_its_place_in_id_order`
- the service's paging guards removed: four of the five cases of `Invalid_paging_is_refused`. A `pageSize` of 0 is still refused, by `PaginatedItems`.
- brands ordered by name: the brand list, ordering and snapshot tests
- brands, or pages, without an `ORDER BY`: only `Brands_and_types_are_read_in_id_order` or `Paging_reads_only_the_rows_of_the_page_in_id_order`. SQL Server still returned the rows in ID order.
- a delete that passes `CancellationToken.None`, which CA2016 accepts because a token is passed: `Cancelled_operations_change_nothing`
- `TotalPages` with the legacy `decimal` division and `int` cast: the two `long.MaxValue` cases of `Total_pages_hold_every_item`
- `PaginatedItems` without its page-size guard: `Page_larger_than_its_size_is_refused`

### Alternatives considered

- **Keeping the synchronous interface until Stage 8.** Plan decision 6 rules it out, and CA1849 fails a synchronous EF Core call inside async code.
- **An optional `CancellationToken`** (`= default`). Easier to call, and just as easy to forget.
- **`IAsyncEnumerable` for the lists.** There are 5 brands and 4 types, and the API caps a page at 100 items (Stage 7.5). A list that is read before it returns also keeps the query out of serialization (D17).
- **The entity as the input of create and update**, as in the legacy service. It carries `Id` and `PictureFileName`, the two fields that a caller must not set, and EF Core would insert a non-zero ID as it is.
- **Read, copy the fields, `SaveChanges`** for update. Two round trips, and a row deleted in between throws `DbUpdateConcurrencyException`. The legacy schema has no row version, so there is no concurrency token for such an update to check either. If one arrives, the change stays inside the service.
- **A generic repository, or `IQueryable` for the endpoints.** A repository adds a layer that nothing else uses. `IQueryable` would carry EF Core into the endpoints, and an in-memory implementation could not translate it the same way.
- **Response DTOs from the service.** The entities hold only stored data ([ADR-0010](#adr-0010-data-model)), and the response contracts of Stage 7 map from them.
- **Not-found exceptions.** A `null` or `false` is cheaper, and the endpoints map it directly.
- **A database per contract test.** The same isolation as the transaction, for the cost of creating and migrating a database for each test.

### Consequences

- The endpoints of Stage 7 use `ICatalogService`, not the `DbContext`.
- The in-memory service of Stage 5.2 must pass the same suite: results that belong to the caller, IDs that are never given again, refused unknown brands and types, and cancelled tokens.
- Nothing in the app uses the service until Stage 5.3 registers it.
- The exception for an unknown brand or type differs between create and update, and may differ between implementations. An endpoint that relied on it would answer differently in mock mode, so the endpoints are expected to check first.
- The 50-character limit on names and the two decimal places of prices are enforced by the database only: `nvarchar(50)` refuses a longer name, and `decimal(18,2)` keeps two decimal places of a price. Stages 7.6 and 7.7 are expected to validate both (D9, D10).
- [docs/behavior-changes.md](docs/behavior-changes.md) gets no entry: nothing reaches HTTP yet. D2, D6, D11 and D17 change what clients see only when the endpoints of Stage 7 expose them, and those commits record it.

---

## ADR-0016: In-memory catalog service

- **Status:** Accepted
- **Date:** 2026-09-30
- **Plan stage:** 5.2

### Context

- The legacy app runs without a database when `UseMockData` is `true` ([audit: configuration](docs/legacy-audit.md#61-webconfig)). Autofac then registers `CatalogServiceMock` as a singleton, over a `List<CatalogItem>` filled from `PreconfiguredData`, the same data that seeds the database ([audit: components](docs/legacy-audit.md#21-components)).
- [ADR-0001](#adr-0001-migration-scope) keeps the in-memory mode. Stage 5.3 wires it to `Catalog:UseMockData`.
- The audit lists the mock's defects as D15 ([audit: defects](docs/legacy-audit.md#7-defects-and-risks)):
  - One instance serves every request, but the list is a plain `List<T>`, changed by concurrent requests.
  - Every read sets the brand and type of the shared items in place (`ComposeCatalogItems`), and hands those objects to the caller.
  - A new item gets `Max(Id) + 1`: that throws on an empty list, and gives a removed item's ID to the next one.
  - An item with an unknown brand or type is stored, and every later read of the item list throws, because `First()` finds no brand.
- [ADR-0015](#adr-0015-async-first-catalog-service) defines the service contract, and a shared suite that each implementation must pass.

### Decision

**1. `InMemoryCatalogService`** (`Catalog/InMemoryCatalogService.cs`) implements `ICatalogService` in memory. Stage 5.3 registers it as a singleton in mock mode. It loses its changes when the process ends, as the legacy mock did.

**2. The same starting data as a new database.** The brands, types and sample items move out of the EF Core mapping and the seeder into `Catalog/PreconfiguredData.cs`, named after the legacy class. The migrations get the brands and types from it through `HasData`, the seeder gets the items, and the in-memory service gets all three. The items take IDs 1–12 in list order, as HiLo gives them in a new database. `SampleItemSeeder.CreateItems` (ADR-0013) becomes `PreconfiguredData.CatalogItems`. The model and the migration snapshot do not change.

**3. Thread safety.** A `System.Threading.Lock` guards the items and the last ID given. Every read and write of the items holds it. Brands and types are reference data that nothing changes, so they need no lock.

**4. What it fixes of D15**

| Legacy mock | `InMemoryCatalogService` |
|---|---|
| A plain list changed by concurrent requests | Items in a `SortedDictionary` by ID, under the lock |
| Reads set the brand and type of the shared items and return them | Reads return copies, with copies of the brand and the type. Create returns a copy, made under the lock, because an update may already change the stored item. |
| `Max(Id) + 1`: throws on an empty catalog, reuses a removed item's ID | A counter that starts after the sample items and only goes up |
| Stores an item with an unknown brand or type, after which the item list fails | Refuses it with an `ArgumentException`, as the foreign keys refuse it |
| Updates replace the stored object with the posted one | Updates write the nine fields of `CatalogItemFields` into the stored item, as `CatalogService` does |

**5. Asynchronous like `CatalogService`.** The operations do no I/O, so they complete synchronously, but they end the way the EF Core service's do: a token that is already cancelled gives a cancelled task, and an exception, an argument check's included, is in the returned task rather than thrown by the call.

**6. Prices as the database holds them.** The `decimal(18,2)` column gives back a price with two decimal places, so 8 reads as 8.00, which JSON shows. The in-memory service stores a price the same way. The item that create returns is not read back: the EF Core service returns the price as it was given, and the in-memory service as it is stored. The suite compares only prices that are read back. More than two decimal places is left to the API's validation (Stage 7.6).

**Tests**

`InMemoryCatalogServiceTests`, in the unit tests, runs the whole contract suite of ADR-0015 against a new instance per test, without Docker. The new tests:

| Test | What it pins |
|---|---|
| `CatalogServiceContractTests.Emptied_catalog_takes_new_items`, against both implementations | With every item removed, a page is empty with 0 pages, and a new item gets an ID that no sample item had (the legacy `Max(Id) + 1` threw). |
| `CatalogServiceContractTests.Prices_are_read_back_with_two_decimal_places`, against both implementations | A created price of 8 and an updated price of 8.5 read back as `8.00` and `8.50`. |
| `InMemoryCatalogServiceTests.Concurrent_writes_and_reads_keep_the_catalog_consistent` | 200 parallel writers each create an item, find it on a page, update it, read it back updated, and every second one removes it. Every ID is distinct, and the catalog ends with the sample items and the kept items, updated. |
| `PreconfiguredDataTests` (4 tests) | The brands, the types and the items, numbered in list order, equal `seed-data.json`, and each call returns new instances. The item test moved here from `SampleItemSeederTests` (ADR-0013). |

Each of these deliberate breaks failed the intended tests:

- no mutual exclusion, with each `lock` on a new object: `Concurrent_writes_and_reads_keep_the_catalog_consistent`, in each of three runs
- new IDs from `Max(Id) + 1`, as in the legacy mock: `Ids_are_never_given_again`, `Emptied_catalog_takes_new_items` and the concurrency test
- reads that return the stored items: `Changing_what_the_service_returned_changes_nothing`
- no check of the brand and the type: the four cases of `Item_with_an_unknown_brand_or_type_is_refused`
- an unsorted `Dictionary` for the items: `Pages_stay_in_id_order_after_removes_and_creates` and the concurrency test
- no check of the cancelled token: `Cancelled_operations_change_nothing`
- prices kept as given: `Prices_are_read_back_with_two_decimal_places`

Not tested:

- A narrower race, such as only the ID counter outside the lock, fails the concurrency test only now and then: in 2–11% of runs of a model of the test that the review ran outside this repository. The test shows that the lock is there, not that every statement is inside it.
- The copy that create returns is made under the lock. The review found it outside. A test would have to time an update of the new ID between the add and the copy.

### Alternatives considered

- **`ConcurrentDictionary` without a lock.** Each operation would be atomic, but a page needs a consistent ordered view, and an update checks, then writes. A lock around a sorted dictionary is simpler, and a mock does not need more throughput.
- **Immutable collections swapped with `Interlocked`.** Reads without a lock, at the price of a retry loop on every write. Not needed at this load.
- **EF Core's in-memory provider**, or SQLite in memory, behind `CatalogService`. Neither runs the `catalog_hilo` sequence ([ADR-0007](#adr-0007-test-strategy)), and mock mode should need no EF Core model at all.
- **Keeping `Max(Id) + 1`.** It reuses IDs, and a reused ID could point a client's cached link at another item.
- **Storing unknown brands and types, and returning items without them.** The in-memory mode would accept data that the database refuses.

### Consequences

- Mock mode behaves like the database for everything the contract suite covers, and the suite runs on every build without Docker.
- A change to the contract has to be implemented twice, and the suite shows where the two differ.
- The sample data lives in one place, `PreconfiguredData`, for the migrations, the seeder and mock mode.
- The mock keeps every item in one process. Several instances of the API in mock mode each have their own catalog, as in the legacy app.

---

## ADR-0017: Built-in dependency injection and mock mode

- **Status:** Accepted
- **Date:** 2026-09-30
- **Plan stage:** 5.3

### Context

- The legacy app builds an Autofac 6.1.0 container in `Application_Start` ([audit: dependency injection](docs/legacy-audit.md#24-dependency-injection-autofac-610)). It scans the MVC and Web API controllers, and registers `ApplicationModule`, which holds four registrations:

  | Legacy registration | Lifetime |
  |---|---|
  | `ICatalogService` → `CatalogServiceMock` when `UseMockData` is `true`, otherwise `CatalogService` | Singleton (mock) / per lifetime scope |
  | `CatalogDBContext` | Per lifetime scope |
  | `CatalogDBInitializer` | Per lifetime scope, but resolved from the root container |
  | `CatalogItemHiLoGenerator` | Singleton |

- Two resolvers are set, one for MVC and one for Web API. `UseMockData` is read twice with `bool.Parse`, so a missing or malformed value stops `Application_Start` before any route is registered ([audit: appSettings consumers](docs/legacy-audit.md#62-appsettings-consumers)).
- Autofac.Mvc5 4.0.2 runs outside its supported Autofac range through a binding redirect, and the build warns about it (D25).
- Plan decision 3 drops Autofac for the built-in container. [ADR-0009](#adr-0009-configuration) maps `UseMockData` to `Catalog:UseMockData`, a typed option that arrives with its first consumer.
- [ADR-0010](#adr-0010-data-model) makes a connection string mandatory, and [ADR-0013](#adr-0013-seeding-migrate-on-startup-and-readiness) adds migrate-on-startup and a readiness check of the database. Both ADRs leave it to this stage to fit mock mode, which needs no database, around them.
- ASP.NET Core validates scopes and service registrations only in the Development environment by default.

### Decision

**1. `AddCatalogServices`** (`Catalog/CatalogServiceCollectionExtensions.cs`) replaces `ApplicationModule`. `Program.cs` calls it once.

| Legacy registration | Now |
|---|---|
| `ICatalogService` → `CatalogServiceMock`, singleton | `InMemoryCatalogService`, singleton ([ADR-0016](#adr-0016-in-memory-catalog-service)) |
| `ICatalogService` → `CatalogService`, per scope | `CatalogService`, scoped ([ADR-0015](#adr-0015-async-first-catalog-service)) |
| `CatalogDBContext`, per scope | `CatalogDbContext` through `AddDbContext`, scoped ([ADR-0010](#adr-0010-data-model)) |
| `CatalogDBInitializer` | Gone: migrations, the seeder and migrate-on-startup ([ADR-0011](#adr-0011-ef-core-migration-strategy), [ADR-0013](#adr-0013-seeding-migrate-on-startup-and-readiness)) |
| `CatalogItemHiLoGenerator`, singleton | Gone: EF Core's `UseHiLo` ([ADR-0010](#adr-0010-data-model)) |
| Controller scanning, two resolvers | Gone: Minimal API endpoints take their services as parameters from the one container (Stage 7) |

**2. `Catalog:UseMockData`** is bound to `CatalogOptions` with `BindConfiguration`, `ValidateDataAnnotations` and `ValidateOnStart`, as ADR-0009 sets out. It is `false` in `appsettings.json`, as `UseMockData` was in `Web.config`, and no other committed file sets it.

The mode decides what is registered, so `AddCatalogServices` also reads the section while it registers the services, before the container exists. A malformed value, such as `yes`, stops the host at startup with the binder's message, which names `Catalog:UseMockData`. A missing value means `false`: the legacy app did not start without one, but the new default lives in `appsettings.json`.

The mode is therefore fixed from the configuration that `Program.cs` sees. A source added after it, such as a test's `ConfigureAppConfiguration`, would reach `IOptions<CatalogOptions>` but not the registrations, so nothing branches on the mode through the options, and tests set it with `UseSetting`, which `Program.cs` sees.

**3. Mock mode registers nothing of the database.**

| | Database mode | Mock mode |
|---|---|---|
| `ICatalogService` | `CatalogService`, scoped | `InMemoryCatalogService`, singleton |
| `CatalogDbContext` and `ConnectionStrings:CatalogDb` | Registered; the host stops without a connection string | Not registered, not read |
| Migrate on startup (`Database` section) | Registered; the setting and its Development-only rule apply | Not registered, not read |
| `/health/ready` | Runs the `catalog-database` check | Runs no check, so it answers `200 Healthy` |

So `dotnet run` in Development with `Catalog__UseMockData=true` starts without any database server, although `appsettings.Development.json` turns migrate-on-startup on. The `Database` settings are not validated in mock mode, because nothing uses them.

**4. Scope validation in every environment.** `Program.cs` sets `ValidateScopes` and `ValidateOnBuild` on the default service provider:

- A scoped service, such as `CatalogService` or the `DbContext`, cannot be resolved from the root provider, where it would live as long as the app.
- Every registration by type is checked when the container is built, so a singleton that would hold a scoped service stops the host at startup, before anything resolves it. Registrations by factory are checked only when they are resolved.

ASP.NET Core turns both on only in Development. The build check runs once at startup. The scope check runs on each resolution, and costs a reference comparison outside the root provider.

**Tests**

Integration tests, `CatalogServiceRegistrationTests`:

| Test | What it pins |
|---|---|
| `Database_mode_gives_each_scope_its_own_catalog_service` | `CatalogService`, the same instance within a scope and another in the next scope, serving the sample items. |
| `Mock_mode_serves_the_sample_data_without_a_database` (Development, Testing, Production) | With a blank connection string, migrate-on-startup on in every environment, and no database: `InMemoryCatalogService`, one instance for the root and every scope, no `CatalogDbContext`, no migrate-on-startup service, the sample items, and `/health/ready` `200 Healthy`. |
| `Host_does_not_start_with_a_malformed_use_mock_data` | `yes` stops the host with a message that names `Catalog:UseMockData`. |
| `Scoped_services_cannot_be_resolved_from_the_root_provider` | In the Testing environment, resolving `ICatalogService` from the root throws. |
| `Host_does_not_start_with_a_singleton_that_holds_a_scoped_service` | A singleton that takes `ICatalogService` stops the host at startup, although nothing resolves it. |

`ConfigurationTests.Committed_settings_keep_mock_mode_off` (with and without `appsettings.Development.json`) reads only the committed files, as the other settings tests do.

Unit tests, `CatalogServiceCollectionExtensionsTests`, check the registrations without a host:

| Test | What it pins |
|---|---|
| `Database_mode_registers_a_scoped_catalog_service_and_the_database` | With no `Catalog` section: `CatalogService` scoped, `CatalogDbContext`, the migrate-on-startup service and the `catalog-database` check. |
| `Mock_mode_registers_one_in_memory_catalog_and_nothing_of_the_database` | With no connection string: `InMemoryCatalogService` as a singleton, and no `CatalogDbContext`, hosted service or health check. |
| `Explicit_false_is_database_mode` (`false`, `False`) | The binder's spellings of `false`. |
| `Database_mode_still_needs_a_connection_string` | ADR-0010's check still applies outside mock mode. |
| `Malformed_use_mock_data_is_refused_while_registering` | `yes` throws while the services are registered, naming the setting. |

Each of these deliberate breaks failed the intended tests:

- `ValidateScopes` off: `Scoped_services_cannot_be_resolved_from_the_root_provider`, and `Host_does_not_start_with_a_singleton_that_holds_a_scoped_service`, because the build check finds a captive scoped service only with scope validation on
- `ValidateOnBuild` off: `Host_does_not_start_with_a_singleton_that_holds_a_scoped_service`
- mock mode that also registers the `DbContext`, or migrate-on-startup, or the readiness check of the database, or a mode that is ignored: the three cases of `Mock_mode_serves_the_sample_data_without_a_database` and `Mock_mode_registers_one_in_memory_catalog_and_nothing_of_the_database`
- a malformed value read as `false`: `Malformed_use_mock_data_is_refused_while_registering`. The host test still passed, because `ValidateOnStart` refuses the value too.
- mock mode on in `appsettings.json`: `Committed_settings_keep_mock_mode_off`. The other tests passed, because `CatalogApiFactory` sets the mode.

A Development host was also run by hand with `Catalog__UseMockData=true` and a connection string to a server that does not exist: it started, and `/health/live` and `/health/ready` answered `200 Healthy`. Without mock mode, it migrated its LocalDB database at startup and `/health/ready` answered 200.

### Alternatives considered

- **Keeping Autofac** through `Autofac.Extensions.DependencyInjection`. Four registrations need none of its features: modules, assembly scanning, property injection or decorators. It would be one more package to keep inside its supported range, which the legacy app did not manage (D25).
- **Registering both implementations, and choosing one per resolution** with a factory that reads `IOptions<CatalogOptions>`. The mode would still need the connection string, the migration service and the readiness check registered in both modes, or conditions in each of them.
- **Keyed services** (`AddKeyedScoped`). The endpoints would have to name a key, and only one implementation is ever used by a process.
- **Refusing to start when mock mode meets database settings.** Development sets migrate-on-startup and a connection string by default, so every mock run in Development would first need them turned off.
- **Leaving scope validation to the Development default.** A captive dependency would then only fail where developers run the app, and tests run in Testing.

### Consequences

- The legacy DI packages (`Autofac`, `Autofac.Mvc5`, `Autofac.WebApi2`) have no counterpart. They leave with the legacy project in Stage 11.3.
- The endpoints of Stage 7 take `ICatalogService` as a parameter and work in both modes.
- In mock mode, readiness says nothing about a database, because there is none. A deployment that turns mock mode on by mistake is ready, and serves the sample data.
- `CatalogApiFactory` still gives each test class a database, and sets `Catalog:UseMockData` to `false`, so a developer's `Catalog__UseMockData` environment variable cannot switch the integration tests to mock mode. A test that wants mock mode sets it with `UseSetting`, which wins.
- For the plan's end-to-end check, "run again with `Catalog__UseMockData=true` and no database", the host starts without a database from this stage. The endpoint checks follow in Stage 7.

---

## ADR-0018: Logging with Serilog

- **Status:** Accepted
- **Date:** 2026-10-01
- **Plan stage:** 6.1

### Context

- The legacy app logs with log4net 2.0.10, configured from `log4Net.xml` ([audit: logging](docs/legacy-audit.md#64-logging-log4net)):
  - Every level (`ALL`), in every environment.
  - One `RollingFileAppender` writing `logFiles\myapp.log`, resolved against the app root, so the file sits inside the IIS web root.
  - It rolls by size at `10MB`, which log4net reads as 10,485,760 bytes. It keeps 5 backups (`myapp.log.1`–`.5`) beside the active `myapp.log`, so at most 6 files.
  - A multi-line text layout. Its `%property{activity}` never matches the `activityid` key that `Application_BeginRequest` sets, so it always prints `(null)`.
- Nothing logs at WARN or above, and no code path logs an exception. The call sites are DEBUG lines (`WebApplication_BeginRequest` on every request, and `Now disposing` twice per request in `CatalogController`) and INFO `Now loading...` and `Now processing...` lines in the MVC controllers, which Stage 7 replaces.
- The audit's D18 lists the logging defects: the `activity` mismatch, a configuration file name that works only on a case-insensitive file system, every level everywhere, no exception ever logged, unsanitized input that can forge lines, and the file inside the web root ([audit: defects](docs/legacy-audit.md#7-defects-and-risks)).
- NuGet audit flags log4net 2.0.10 (NU1902, moderate, GHSA-4f7c-pmjv-c25w) ([audit: version conflicts](docs/legacy-audit.md#33-version-conflicts)).
- Plan decision 4: Serilog behind `ILogger<T>`, registered with `AddSerilog` and without a static bootstrap logger, so test hosts stay isolated.
- [ADR-0001](#adr-0001-migration-scope) keeps a size-rolled log file with log4net's limits. [ADR-0003](#adr-0003-non-goals) drops the log4net line format. [ADR-0009](#adr-0009-configuration) maps `log4Net.xml` to Serilog settings in `appsettings.json`.
- Until this stage, the new host used the default ASP.NET Core providers (console, debug and EventSource at Information and above, and the event log on Windows from Warning), and wrote no file.

### Decision

**1. One package, `Serilog.AspNetCore` 10.0.0.** It brings Serilog 4.3, `AddSerilog` (`Serilog.Extensions.Hosting`), `ReadFrom.Configuration` (`Serilog.Settings.Configuration`), and the console sink, the file sink and the compact JSON formatter that the settings name. Stage 6.2 uses its request logging. The settings name these assemblies, which come in transitively. The tests build loggers from the committed settings, so an upgrade that drops one of them fails the tests.

**2. Registration.** `AddCatalogLogging` (`Logging/LoggingServiceCollectionExtensions.cs`) calls `AddSerilog` with `preserveStaticLogger: true`:

- The host's `ILoggerFactory` is Serilog's, so every `ILogger<T>` writes to Serilog, and the default providers are no longer used.
- The logger is built from the host's configuration, when the container first resolves the logger factory, while the host is built.
- The static `Log.Logger` stays Serilog's silent default. With the default `preserveStaticLogger: false`, every host would set it to its own logger, the loggers that a host creates would write to the sinks of the last host to start, and the first host to stop would close them. A test process runs many hosts at once.
- There is no bootstrap logger. An exception that stops the host before its container exists, such as a missing connection string, reaches only the standard error output, as an unhandled exception.
- `Program.cs` turns on Serilog's `SelfLog` on the standard error output. Without it, Serilog drops events silently when a sink fails, for example when it cannot open the log file.

**3. Settings.** The `Serilog` section of `appsettings.json`:

| Setting | Value | Why |
|---|---|---|
| `Using` | `Serilog.Sinks.Console`, `Serilog.Sinks.File` | The assemblies that hold the sinks. |
| `MinimumLevel:Default` | `Information` | The level the host logged at before this stage. log4net logged every level, but its DEBUG events were the per-request line, which Stage 6.2 replaces, and `Now disposing`, which leaves with the MVC controller. |
| `MinimumLevel:Override:Microsoft.EntityFrameworkCore.Database.Command` | `Warning` | EF Core logs the SQL of every command at Information. The legacy app logged no SQL. A failed command is still logged, at Error. |
| `Enrich` | `FromLogContext` | Properties that code pushes with `LogContext.PushProperty` reach every event in their scope. The scopes of `ILogger.BeginScope`, such as ASP.NET Core's request ID, are added by Serilog's logger provider without it. |
| `WriteTo:Console` | A one-line text template: time, level, source context, message | For developers, and for container log collectors. |
| `WriteTo:File` | See below | log4net's file. |

The sinks under `WriteTo` are keyed by name, not listed in an array. One setting then changes one sink whatever the order, for example the environment variable `Serilog__WriteTo__File__Args__path`. In an array the key would be the sink's position.

Serilog watches `MinimumLevel:Default` and each override present at startup, so a change to their values in `appsettings.json` applies without a restart. Adding or removing an override needs a restart.

**4. The log file, against log4net's.**

| | log4net | Serilog |
|---|---|---|
| Path | `logFiles\myapp.log`, against the app root, inside the web root | `logFiles/myapp.log`, against the content root, which the API does not serve |
| Roll | By size, at 10,485,760 bytes. Never by date. | The same (`rollOnFileSizeLimit`, `fileSizeLimitBytes`, no `rollingInterval`) |
| Files kept | `myapp.log` and 5 backups | 6 files, the active one included (`retainedFileCountLimit`) |
| Names | The active file is always `myapp.log`. Backups shift to `myapp.log.1`–`.5`, `.1` the newest. | `myapp.log`, then `myapp_001.log`, `myapp_002.log` and so on. The active file has the highest number, and the oldest file is deleted. |
| Size of a full file | log4net checks the size before each event, so a file ends less than one event above the limit. | The same |
| Format | Multi-line text | Compact log event format (CLEF): one JSON object per line |
| Configuration | `log4Net.xml` | The `Serilog` section, overridable per environment and by environment variables |

Serilog resolves a relative path against the working directory. The content root is usually the working directory, but not for a Windows service, which starts in `system32` and gets the app folder as its content root, nor for a host that sets its content root. `AddCatalogLogging` resolves the path against the content root, the folder whose `appsettings.json` the app reads, as log4net resolved its file against the app root. Environment variables in the path, which Serilog expands, are expanded first. With `dotnet run` the file is `src/eShop.Catalog.Api/logFiles/myapp.log`, which git ignores. A deployment can set an absolute path.

**5. Format.** The file uses `CompactJsonFormatter`:

- `@t`, the time in UTC
- `@mt`, the message template, and `@r`, the renderings of formatted values
- `@l`, the level, left out for Information
- `@x`, the exception
- `@tr` and `@sp`, the trace and span IDs
- every property of the event, the source context and the scope properties included

The console sink writes text: `[HH:mm:ss LVL] SourceContext: message`, and the exception on the following lines.

**6. Trace and span IDs.** Serilog takes the trace and span IDs of `Activity.Current` for every event, those of `ILogger<T>` included, without an enricher package. ASP.NET Core starts an activity for each request, from the caller's W3C `traceparent` header when there is one. So every event of a request carries the request's trace ID. The trace ID is the counterpart of log4net's `activityid`, which never printed. Stage 6.2 adds the request event, and with it replaces `activityid` and `requestinfo`.

**7. Test hosts.** In the test hosts the content root is the API's source folder, and every host would write to the same file there. `CatalogApiFactory` gives each factory a temporary directory, and deletes it when the factory is disposed. The factory's own host writes `myapp.log` in it, and each host that `WithWebHostBuilder` derives writes a file of its own beside it: two hosts writing to one file overwrite each other's lines on Linux, and on Windows the second one moves to another file.

It sets the path with a configuration source (`UseLogFile`), not with `UseSetting`. `WebApplicationFactory` passes host settings to `Program` as command-line arguments, and it passes each parent section of a key too, as an empty value. Serilog takes a `WriteTo` entry with a value for the name of a sink. Without the resolution of decision 4, as in the deliberate break below, the empty `Serilog:WriteTo:File` was read as a sink named "", which does not exist, and the file sink was left out, with only a `SelfLog` message. The resolution hides the empty values, but only because the configuration it builds treats empty values as missing, and the tests should not rely on that. A deployment's settings have no such empty sections.

**Tests**

Unit tests, `LoggingServiceCollectionExtensionsTests`:

| Test | What it pins |
|---|---|
| `Relative_file_path_is_resolved_against_the_content_root` | `logFiles/myapp.log` becomes a path under the content root. |
| `Absolute_file_path_is_kept` | An absolute path is not changed. |
| `Environment_variables_are_expanded_before_the_path_is_resolved` | A variable that holds an absolute path does not end up under the content root. |
| `Configuration_without_a_file_path_is_returned_as_it_is` | Without a file path, the configuration is returned unchanged, as the same object. |
| `Resolved_configuration_reads_through_to_the_settings_and_passes_on_their_reloads` | A reload of the settings reaches the configuration that Serilog reads, so the levels still follow it. |

Integration tests, `LoggingTests`:

| Test | What it pins |
|---|---|
| `Committed_settings_log_from_Information_to_the_console_and_to_the_log4net_file` | The committed levels (Information, and Warning for EF Core's commands, the only override), sinks and enrichment, and the file: path, size limit, roll on size, 6 files, no date rolling, CLEF. |
| `Log_file_rolls_at_10_MiB_and_keeps_the_6_newest_files` | The committed file sink alone, written into a seventh file in 64 KiB events: `myapp_001.log`–`myapp_006.log` remain, and each full file ends between 10 MiB and one event above it. |
| `Host_writes_each_event_as_a_CLEF_line_with_its_trace_span_and_log_context` | An `ILogger<T>` event of the test host, in its file: the template, the trace and span IDs of the current activity, the source context, a property pushed on the log context, and no `@l` for Information. |
| `Each_host_writes_to_its_own_sinks_and_leaves_the_static_logger_silent` | Two hosts at once: each probe is only in its own host's file, and `Log.Logger` is not enabled even for Fatal. |
| `Relative_log_file_path_is_resolved_against_the_content_root` | A host whose content root is a temporary directory, with the committed relative path, writes its file under that directory. |

Each of these deliberate breaks failed the intended tests:

- `preserveStaticLogger: false`: `Each_host_writes_to_its_own_sinks_and_leaves_the_static_logger_silent`, and `Host_writes_each_event_as_a_CLEF_line_with_its_trace_span_and_log_context`, whose event went to another host's file. A first version of the isolation test, which only looked at `Log.Logger` after its class's host had started, passed this break: another host had reset the static logger when it stopped. The test now runs two hosts at once.
- the registration without the resolution against the content root: `Relative_log_file_path_is_resolved_against_the_content_root`
- environment variables expanded after the resolution: `Environment_variables_are_expanded_before_the_path_is_resolved`
- 5 retained files: `Log_file_rolls_at_10_MiB_and_keeps_the_6_newest_files` and `Committed_settings_log_from_Information_to_the_console_and_to_the_log4net_file`
- 10,000,000 bytes for the limit: the same two tests. With 512 KiB events, the first version of the rolling test passed it, so its events are now 64 KiB.
- no roll on size: the same two tests. The rolling test first waited for a seventh file that never came, so it now stops after eight files' worth of events.
- no `FromLogContext`: `Host_writes_each_event_as_a_CLEF_line_with_its_trace_span_and_log_context` and the committed-settings test
- `RenderedCompactJsonFormatter`: the same two tests

Development hosts were also run by hand, on LocalDB and in mock mode. The console showed the startup and request events, and `src/eShop.Catalog.Api/logFiles/myapp.log` held them as CLEF. A request with a `traceparent` header logged its events with that trace ID in `@tr`. EF Core's SQL appeared only with the command-line override that the README shows.

### Alternatives considered

- **log4net 3 through `Microsoft.Extensions.Logging.Log4Net.AspNetCore`.** It would keep `log4Net.xml` and the file names, but the settings would stay outside `appsettings.json`, the events would stay text, and plan decision 4 chose Serilog.
- **The built-in providers and a third-party file provider.** ASP.NET Core has no general file provider for `ILogger` (`AddW3CLogging` writes only W3C request lines), so a third-party package would be needed anyway, and plan decision 4 chose Serilog.
- **A static bootstrap logger** (`CreateBootstrapLogger` and `UseSerilog`). It would also log the failures before the container exists, but it is process-wide state that every test host would share. Plan decision 4 rules it out.
- **The log4net layout as a text template.** [ADR-0003](#adr-0003-non-goals) drops the line format, and text loses the properties of each event.
- **`RenderedCompactJsonFormatter`.** It writes the rendered message instead of the template, but it quotes every string value in the message, as in `Now listening on: "http://localhost:5043"`.
- **The `Serilog.Enrichers.Span` package.** Serilog has taken the trace and span IDs itself since version 3.1.
- **The working directory, or the build output folder, for a relative path.** The working directory is not the content root for a Windows service, and the file would then land in `system32`. The output folder is not where log4net put the file, and with `dotnet run` it is under `bin`.

### Consequences

- log4net and its advisory leave with the legacy project in Stage 11.3.
- A tool that follows `myapp.log` has to follow the file with the highest number instead.
- The `Logging` section of ASP.NET Core is not read. The levels are under `Serilog:MinimumLevel`.
- The sinks are configured only in `appsettings.json`. A host whose content root lacks that file, for example a published app started from another working directory, logs nothing at all, where the default providers still wrote to the console.
- The log file's folder must be writable. Otherwise the host runs, writes no file, and Serilog's `SelfLog` reports every event it could not write on the standard error output. A container that runs as a user without write access to the app folder needs an absolute path on a writable volume.
- Of D18, this stage fixes the level, the file in the web root, the activity mismatch (with trace IDs), the configuration file name, and forged lines in the file, because JSON escapes line breaks. Two parts stay open: exceptions, which the request logging of Stage 6.2 and the error handling of Stage 7.1 log, and the console, whose template writes string values as they are, line breaks included.
- Serilog's request logging writes to the static logger unless it is given one, so Stage 6.2 gives it the host's logger.
- The console and file sinks write on the calling thread, as log4net did. Stage 8.1, which sweeps the request paths for synchronous I/O, now names the log sinks, and decides whether they move behind a background queue (`Serilog.Sinks.Async`).
- The test hosts' log files live only as long as their factory, except, on Windows, the file of a host that failed to start. A test that changes a `Serilog` setting uses a configuration source, as `UseLogFile` does, not `UseSetting`.

---

## ADR-0019: Request logging and application log events

- **Status:** Accepted
- **Date:** 2026-10-01
- **Plan stage:** 6.2

### Context

- The legacy `Application_BeginRequest` (`Global.asax.cs`) sets two log4net properties for each request, then logs `WebApplication_BeginRequest` at DEBUG ([audit: logging](docs/legacy-audit.md#64-logging-log4net)):
  - `requestinfo`: the raw URL and the user agent, which the layout prints with every event of the request
  - `activityid`: a correlation GUID, which the layout never prints, because it asks for `activity`
- The MVC controllers log `Now loading...` and `Now processing...` lines at INFO from interpolated strings, so the IDs, the paging values and the item name in them are text, not properties. Nothing logs a request's status or its duration, and the app logs no exception (D18).
- [ADR-0018](#adr-0018-logging-with-serilog) puts Serilog behind `ILogger<T>`, adds the trace and span IDs to every event, and leaves the static `Log.Logger` silent. Serilog's request logging middleware writes to the static logger unless it is given one.
- ADR-0018 counts exceptions among the parts of D18 that stay open. That overstates it: Kestrel already logs an exception that escapes the app, at Error, and did before Stage 6.
- Until this stage, ASP.NET Core logged three or four events at Information for each request: hosting's `Request starting` and `Request finished`, and either routing's `Executing endpoint` and `Executed endpoint` or, for a request that no endpoint took, hosting's `Request reached the end of the middleware pipeline without being handled by application code`.
- Plan 6.2: request logging in place of `Application_BeginRequest` and its two properties, and source-generated `LoggerMessage` methods.

### Decision

**1. One event per request.** `UseCatalogRequestLogging` (`Logging/RequestLoggingApplicationBuilderExtensions.cs`) sets up Serilog's `UseSerilogRequestLogging`, and `Program.cs` adds it as the app's first middleware. `WebApplication` runs routing before the app's middleware and the endpoints after it, so the request logging wraps every request that routing passes on, matched or not, with the matched endpoint known. The event is written when the request completes:

| log4net | Now |
|---|---|
| `WebApplication_BeginRequest` at DEBUG, when the request begins | `HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms`, Serilog's template, when the request completes |
| `requestinfo`, the raw URL and the user agent, printed with every event of the request | `RequestPath` in the message, and `QueryString` and `UserAgent` as properties of the request event. The other events of the request share its trace ID instead. |
| `activityid`, never printed | `@tr` and `@sp` on every event ([ADR-0018](#adr-0018-logging-with-serilog)). A caller's W3C `traceparent` header sets the trace ID. |

The middleware gets the host's Serilog logger, `options.Logger`, which `AddSerilog` registers.

Stage 7 does not port the controllers' `Now loading...` and `Now processing...` lines. The request event already has the method, the path and the query string of each request. What it does not show, such as the name of a created item, is left to Stage 7's endpoints, as `[LoggerMessage]` events where they need one.

**2. Levels of the request event.** `RequestLevel` decides, in this order:

| Request | Level |
|---|---|
| It threw, unless it was cancelled because the client went away | Error, with the exception in `@x` |
| Its endpoint has `RequestLogLevel` metadata | That level, whatever the status |
| Status 500 or above | Error |
| Any other | Information. A 4xx is the client's error, not the server's. |

A request that the client aborted, such as a probe that gave up, ends with an `OperationCanceledException` while `RequestAborted` is cancelled. That is not the server's error, so the other rules decide its level. Any other exception, a cancellation that the client did not cause included, is an Error.

The health check endpoints carry `RequestLogLevel(Debug)`. Probes call them every few seconds, so at Information they would fill the log. A failing check is still logged, at Error, by ASP.NET Core's health check service. Endpoint metadata, rather than a test of the path, keeps the rule on the two endpoints, and off unknown paths under `/health`.

**3. ASP.NET Core's own request events off.** `Serilog:MinimumLevel:Override:Microsoft.AspNetCore` is `Warning`. The request event replaces hosting's and routing's request events. ASP.NET Core's warnings and errors are still logged. Hosting still starts the request's activity, so the trace ID is still there.

**4. Application events through `[LoggerMessage]`.**

- The app logs through source-generated `[LoggerMessage]` methods: static partial methods in the class that logs. The template is checked at build time, its values become properties, and nothing is formatted when the level is off. log4net's interpolated strings kept the values only as text.
- The method name is the event name (`EventId.Name`). The methods set no event ID: the generator derives a stable one from the name.
- CA1848 already flags a call such as `logger.LogInformation(...)` as a warning in the `Recommended` analysis mode, which the build treats as an error ([ADR-0006](#adr-0006-solution-structure-and-build-conventions)). `.editorconfig` pins it as an error, beside the async analyzers, so the rule stays if the analysis mode changes.
- The first event is `MockModeIsOn`, a Warning that `MockModeWarning`, a hosted service, logs once when the host starts. `AddCatalogServices` registers it in mock mode only. A host in mock mode needs no database and is ready at once ([ADR-0017](#adr-0017-built-in-dependency-injection-and-mock-mode)), so without it nothing would show a deployment that turned mock mode on by mistake.

**Tests**

Unit tests:

| Test | What it pins |
|---|---|
| `RequestLoggingApplicationBuilderExtensionsTests.Status_sets_the_level_when_the_endpoint_sets_none` | 200, 400 and 404 are Information, and 500 and 503 are Error. |
| `RequestLoggingApplicationBuilderExtensionsTests.Endpoint_level_applies_whatever_the_status` | Debug metadata gives Debug for 200 and 503. |
| `RequestLoggingApplicationBuilderExtensionsTests.Request_that_threw_is_an_Error_whatever_the_endpoint_level` | An exception gives Error, with Debug metadata too. |
| `RequestLoggingApplicationBuilderExtensionsTests.Request_cancelled_because_the_client_went_away_is_not_an_Error` | An `OperationCanceledException` with `RequestAborted` cancelled gives Information, or Debug with Debug metadata. |
| `RequestLoggingApplicationBuilderExtensionsTests.Request_cancelled_while_the_client_waits_is_an_Error` | An `OperationCanceledException` without an aborted request gives Error. |
| `RequestLoggingApplicationBuilderExtensionsTests.Health_checks_ask_for_Debug` | `ProbeRequestLogLevel`, which both health check endpoints carry, is Debug. |
| `CatalogServiceCollectionExtensionsTests` (amended) | Mock mode's one hosted service is `MockModeWarning`, and database mode does not register it. |

Integration tests:

| Test | What it pins |
|---|---|
| `RequestLoggingTests.Request_is_logged_once_with_its_trace_id_query_string_and_user_agent` | `GET /no-such-route?pageSize=10` with a `traceparent` and a user agent: the event in the host's file, with the template, the method, the path, 404, the time, the query string, the user agent and the caller's trace ID, at Information. It is the only event of that trace: ASP.NET Core's `Request starting` would be logged before it. |
| `RequestLoggingTests.Request_that_throws_is_logged_at_Error_with_the_exception` | A middleware after the endpoints throws: the event is at Error, with status 500 and the exception. |
| `RequestLoggingTests.Health_check_requests_are_logged_at_Debug` (`/health/live`, `/health/ready`) | With Debug events on, each health check request is logged at Debug. |
| `CatalogServiceRegistrationTests.Mock_mode_warns_at_startup_that_changes_are_lost` | A mock-mode host logs `MockModeIsOn` once, at Warning, with the whole message. |
| `CatalogServiceRegistrationTests.Database_mode_does_not_warn_about_mock_mode` | The factory's host does not. |
| `LoggingTests.Committed_settings_log_from_Information_to_the_console_and_to_the_log4net_file` (amended) | The `Microsoft.AspNetCore` override. |

Each of these deliberate breaks failed the intended tests:

- no `options.Logger`: all four cases of `RequestLoggingTests`, which waited in vain for events that went to the silent static logger
- no `UseCatalogRequestLogging` in `Program.cs`: the same four
- no `Microsoft.AspNetCore` override: `Request_is_logged_once_with_its_trace_id_query_string_and_user_agent`, which found `Request starting` in the trace, and the committed-settings test
- no query string and user agent: `Request_is_logged_once_with_its_trace_id_query_string_and_user_agent`
- health check endpoints without the metadata: both cases of `Health_check_requests_are_logged_at_Debug`
- the endpoint's level before the exception: `Request_that_threw_is_an_Error_whatever_the_endpoint_level` with Debug metadata
- Error for a request that the client aborted: both cases of `Request_cancelled_because_the_client_went_away_is_not_an_Error`
- Warning for a server error: the 500 and 503 cases of `Status_sets_the_level_when_the_endpoint_sets_none`
- no `MockModeWarning` in mock mode: `Mock_mode_registers_one_in_memory_catalog_and_nothing_of_the_database` and `Mock_mode_warns_at_startup_that_changes_are_lost`


A call to `logger.LogInformation` failed the build with CA1848, as it already did through the `Recommended` mode.

A host was also run by hand in Production, with a connection string to a server that does not exist. `/health/ready` answered 503 with no request event at Information, and the health check service logged the failing check at Error. A request to an unknown path gave one event, with its query string and user agent, and no event from ASP.NET Core.

### Alternatives considered

- **ASP.NET Core's HTTP logging** (`AddHttpLogging` with `CombineLogs`). It also writes one event per request, at Information. Serilog's middleware sets the level per request, and comes with the package that ADR-0018 chose.
- **An event when the request begins**, as log4net had. It has no status or duration, and it doubles the events. A request that never completes is the case it would cover (see Consequences).
- **ASP.NET Core's request events at Information.** Three or four events for each request, and none of them has both the status and the user agent.
- **The query string in `RequestPath`** (`IncludeQueryInRequestPath`). Each query would make a different path, so events could no longer be grouped by path.
- **Health checks found by their path.** The rule would also catch unknown paths under `/health`, and would drift if a path changed.
- **Error for every exception, cancellations included.** Every client that hangs up, and every probe that gives up, would log an Error with a stack trace.
- **Event ID numbers on every `[LoggerMessage]`.** They would need a registry to stay unique. The generator derives the ID from the name. Two classes can use the same name, and the source context tells their events apart.
- **`LoggerMessage.Define` by hand.** It does the same with more code.

### Consequences

- A request that never completes, such as one that hangs, is not logged. Neither is a request that never reaches the app's middleware, such as one that Kestrel rejects as malformed.
- At the default level, the health checks leave no request events, even when readiness answers 503. A failing check is still logged, at Error, by the health check service. A probe that gives up before the check finishes is logged at Debug, as an aborted request, and the check logs nothing.
- The query string and the user agent are logged, as `requestinfo` logged them. An endpoint that took a secret in its query string would write it to the log. The bearer tokens of Stage 12 travel in the `Authorization` header, which is not logged.
- The console shows the request's path, as the client sent it, but not its query string or user agent, which only the file holds. The console's template writes the path as it is ([ADR-0018](#adr-0018-logging-with-serilog)).
- The request event logs an exception that escapes the app and lets it go on, so Kestrel also logs it, at Error. Stage 7.1 places the exception handler: inside the request logging, the handler logs the exception and the request event sees a 500 without one. Outside it, both log the exception.
- Authentication and authorization middleware that `WebApplication` adds by itself runs before the app's middleware, so a request it rejects would not be logged. Stage 12 calls `UseAuthentication` and `UseAuthorization` itself, after the request logging. Plan 12.1 now says so.
- New application events are `[LoggerMessage]` methods.
- Mock mode now registers one hosted service, beside the in-memory catalog service ([ADR-0017](#adr-0017-built-in-dependency-injection-and-mock-mode)).
