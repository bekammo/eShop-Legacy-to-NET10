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
