# Migration plan: eShopLegacyMVC → .NET 10 ASP.NET Core Web API

This is the working plan for moving the legacy catalog app (`src/eShopLegacyMVC` at the `legacy-final` tag,
ASP.NET Web API 2 + MVC 5 on .NET Framework 4.7.2, plus `src/eShopLegacy.Utilities`) to a .NET 10 ASP.NET Core Web API.

The migration is incremental. Each sub-task below is roughly one commit, and every commit builds and passes
its tests on its own. Tests, ADRs (`DECISIONS.md`), behavior-change entries (`docs/behavior-changes.md`) and
README updates land in the same commit as the change they describe, not in a batch at the end.

Progress is tracked by ticking the checkboxes below.

## Scope

| Topic | Decision |
|---|---|
| API surface | **Full catalog API.** `/api/brands` is migrated. Capabilities that only existed behind the Razor UI are re-exposed as REST endpoints: items (paged list, get, create, update, delete), types and the item picture. The Razor/MVC UI itself is dropped. |
| `GET /api/files` | **Retired.** It returns a BinaryFormatter payload and is not ported. `eShopLegacy.Utilities` is removed at cutover. |
| Wire contract | **Drop-in compatible:**<ul><li>Same routes, verbs and status codes.</li><li>PascalCase JSON (the Web API 2 / Newtonsoft default), used on every endpoint for consistency.</li><li>`DELETE /api/brands/{id}` stays a no-op.</li></ul>Only security- or platform-forced deltas are allowed (e.g. no XML content negotiation), and each is recorded in the behavior-change register. Idiomatic contract changes are deferred to a future v2. |
| Carried-over runtime features | In-memory mode (`UseMockData`) and a size-rolled log file (log4net parity) are kept. The CSV customization seed and Application Insights, which was inert with no instrumentation key, are dropped. |
| Write-endpoint security | The migration keeps the legacy posture (no authentication), recorded as an accepted risk. Stage 12 adds JWT bearer authorization after cutover. |

## Key findings from the legacy code

The full audit is in `docs/legacy-audit.md` (Stage 1). The findings that shape this plan:

**HTTP surface**
- The only real Web API 2 surface is `BrandsController` (`GET /api/brands`, `GET /api/brands/{id}`, a no-op `DELETE /api/brands/{id}`) and `FilesController` (BinaryFormatter).
- Item CRUD exists only in the MVC `CatalogController` (Razor).
- `PicController` serves `GET /items/{catalogItemId:int}/pic`.
- `CatalogController2` is unreachable dead code. Its class name lacks the `Controller` suffix that MVC needs to discover it.

**Data**
- The EF6 schema is created by `CreateDatabaseIfNotExists`.
- Brand and type IDs are `IDENTITY` columns.
- Item IDs come from a hand-rolled HiLo generator over the `catalog_hilo` sequence (start 1, increment 10). This behaves the same as EF Core's `UseHiLo`.

**Other code**
- `ICatalogService` is synchronous and disposes a DbContext that the container owns.
- Autofac is used only for four plain registrations.
- log4net writes a rolling file (10 MB × 5 backups).
- `Web.config` carries the connection string, `UseMockData`, `UseCustomizationData`, and a pinned `en-US` culture that the legacy price validation silently depends on.

**Defects to fix during the migration**
- Path traversal in the picture endpoint.
- Overposting through `EntityState.Modified`.
- `pageSize=0` throws a divide-by-zero, and paging parameters are not validated.
- A missing picture file returns 500.
- The MIME type lookup is case-sensitive.

**Tests**: none. There are no test projects or test files.

## Architecture decisions (each one is written up as an ADR in the commit that implements it)

1. **Side-by-side project, then cutover. No in-place conversion.**
   - System.Web has no .NET 10 equivalent, so an in-place conversion would have no compilable intermediate states.
   - A YARP + `SystemWebAdapters` strangler setup is more machinery than ~10 endpoints justify.
   - The legacy app stays untouched as the reference implementation until Stage 11. A `legacy-final` tag marks the rollback point.
2. **Minimal APIs** over controllers:
   - Endpoint groups per resource, `TypedResults`, and .NET 10 built-in validation.
   - XML content negotiation is the one controller-only feature this gives up.
3. **Built-in dependency injection.** Autofac is dropped.
4. **Serilog behind `ILogger<T>`.** It gives a rolling file sink, structured enrichment and request logging. It is registered via `AddSerilog` with no static bootstrap logger, so test hosts stay isolated.
5. **EF Core 10 code-first migrations**, with an `InitialCreate` that reproduces the EF6 schema (names, nullability, precision, cascades):
   - `UseHiLo("catalog_hilo")` replaces the custom generator.
   - Brands and types are reference data seeded with `HasData`, using their legacy IDs.
   - Sample items are seeded through `UseSeeding`/`UseAsyncSeeding`.
   - The new app uses its own database. Adopting an existing legacy database is a separate, tested baseline procedure.
6. **Async-first port.** No synchronous EF Core code is written only to be converted later. Async analyzers are errors from the first commit, and Stage 8 verifies the result.
7. **OpenAPI via the built-in `Microsoft.AspNetCore.OpenApi`.** The document and a committed snapshot test arrive in Stage 7.1, before the first endpoint, so every endpoint commit shows its contract diff. Swagger UI comes in Stage 9.
8. **Tests:**
   - xUnit v3 on Microsoft.Testing.Platform, and `WebApplicationFactory` through a shared factory.
   - Testcontainers SQL Server with one container per test assembly and one database per test class.
   - No EF InMemory or SQLite: neither can run the HiLo sequence.
9. **Structure:**
   - One API project organized by folders, plus a unit test project and an integration test project.
   - Central Package Management, a `.slnx` solution, and `global.json`.
   - Stop-files in the legacy project folders keep the new repo-wide build settings from affecting the legacy build until cutover.
10. **Order.** Deviations from the usual list, and why:
    - Configuration comes before EF Core.
    - Each typed options class lands with its first consumer.
    - Async is folded into the port.
    - OpenAPI generation comes with the first endpoint.
    - The picture endpoint comes before the item endpoints, because item responses link to it.

---

## Checklist

### Stage 0 — Plan
- [x] Add `MIGRATION_PLAN.md`.

### Stage 1 — Baseline audit (documentation only; legacy code untouched)
- [x] 1.1 Write `docs/legacy-audit.md`. It covers:
  - architecture and request pipeline
  - package inventory, taken from the csproj `PackageReference` items and classified as runtime, UI-only or build-only
  - endpoint inventory
  - the EF6 schema expected from conventions
  - configuration keys and logging
  - defects and risks
  - test status (none)

  Also verify that the legacy solution builds with MSBuild, and record the result.
- [x] 1.2 Characterize the running legacy app against a fresh LocalDB database. Capture:
  - `docs/legacy/schema.sql`, plus a machine-readable `schema.json` (columns, FKs with delete actions, indexes, sequences)
  - golden HTTP exchanges in `docs/legacy/contract/`, each as request (including `Accept`), status, content type and body. Cover JSON vs XML, missing and non-integer IDs, DELETE, `/api/files` headers, `/api`, and pictures.
  - the comparison rules

  Fallback if the app cannot run: a contract derived from the code, clearly labeled as such.
- [x] 1.3 Create `DECISIONS.md` (ADR template) with these ADRs:
  - migration scope
  - wire-contract policy
  - explicit non-goals, each with the trigger that would bring it back
  - deferral of write-endpoint authorization

  Also create `docs/behavior-changes.md`, and update `README.md` with migration status and a documentation index.

### Stage 2 — Scaffolding
- [x] 2.1 Add the build infrastructure and the new project:
  - `global.json` (SDK 10.0.x, Microsoft.Testing.Platform runner)
  - `Directory.Build.props` (net10.0, nullable, implicit usings, warnings as errors except the NuGet audit warnings NU1900–NU1905, pinned analysis level)
  - `Directory.Packages.props`
  - `.editorconfig`, with async analyzers CA2016, CA1849 and CA2012 set to error
  - stop-files in the legacy project folders and in `docs/legacy/capture`
  - `eShop.Catalog.slnx`
  - `src/eShop.Catalog.Api`: a minimal host with `/health/live`

  ADRs: migration strategy; solution structure and build conventions. Acceptance: the new solution builds, and the legacy MSBuild build is still green.
- [x] 2.2 Add `tests/eShop.Catalog.Api.UnitTests` and `tests/eShop.Catalog.Api.IntegrationTests` (xUnit v3) with a shared `CatalogApiFactory`. First test: `/health/live` returns 200. ADR: test strategy. README: test commands.
- [x] 2.3 Add a GitHub Actions workflow that restores, builds and tests (with TRX output) and runs a vulnerable-package check.

### Stage 3 — Configuration
- [x] 3.1 Set up configuration:
  - `appsettings.json` and `appsettings.Development.json`, plus user secrets.
  - `ConnectionStrings:CatalogDb` pointing at a LocalDB database of its own (`eShopCatalog`, without MARS).
  - Convention: typed options are bound with `BindConfiguration` + `ValidateDataAnnotations` + `ValidateOnStart`, and each lands with its first consumer.

  ADR: configuration, including a table that records the fate of every `Web.config` element.

### Stage 4 — Domain & EF Core
- [x] 4.1 Domain and model:
  - Entities (`string? Description`, no UI attributes, `PictureUri` moved out of the entity).
  - `CatalogDbContext` with `IEntityTypeConfiguration<T>` classes that reproduce the EF6 schema: the EF6 PK/FK/index names, `decimal(18,2)`, cascades, and the `catalog_hilo` sequence with `UseHiLo`.
  - `HasData` for brands and types, with the legacy IDs.
  - `AddDbContext`, failing fast when the connection string is missing. The `Testing` environment has no connection string of its own (ADR-0009), so `CatalogApiFactory` sets a placeholder that no test connects to until 4.2.

  Unit tests assert the model metadata.
- [x] 4.2 Add the `dotnet-ef` local tool and the `InitialCreate` migration, plus the Testcontainers fixture (pinned image, one database per test class). Tests:
  - migrations apply cleanly
  - the schema matches `docs/legacy/schema.json`
  - brands and types get their legacy IDs

  ADR: EF Core migration strategy.
- [x] 4.3 Add a baseline procedure for an existing legacy database (`docs/legacy/baseline.sql`). Integration test: apply the legacy schema, run the baseline, then check that EF Core reads and writes work and that HiLo continues from the sequence.
- [x] 4.4 Seeding and startup:
  - a sample-item seeder via `UseSeeding` and `UseAsyncSeeding` (shared logic, idempotent, item IDs from HiLo). It never adds sample items to an adopted legacy database (ADR-0012).
  - config-gated migration on startup (Development only)
  - `/health/ready` with a database check

  Tests:
  - the seeded items point at the correct brands and types
  - seeding twice is idempotent
  - no HiLo collision after seeding

  ADR: seeding and migrate-on-startup policy.
- [x] 4.5 Document the local database options: LocalDB by default, and `compose.yaml` SQL Server using the same pinned image as the tests. Unit tests keep the compose image equal to the tests' image, the `sa` password out of the file and the port on loopback. ADR: local development databases.

### Stage 5 — Application services & DI
- [x] 5.1 Async service layer:
  - `ICatalogService` returns tasks, takes a `CancellationToken`, and is not `IDisposable`.
  - EF Core `CatalogService`: no-tracking ordered reads, server-side brand lookup, and updates that apply explicit fields.
  - `PaginatedItems<T>` with guards.

  Tests: a shared service contract suite (EF Core implementation) and pagination unit tests. ADR: async-first port.
- [x] 5.2 Add a thread-safe `InMemoryCatalogService`, and run the same contract suite against it (no Docker needed).
- [x] 5.3 Add `AddCatalogServices`, which replaces the Autofac `ApplicationModule`:
  - `Catalog:UseMockData` selects the implementation.
  - Mock mode needs no database.
  - Scope validation is on.

  Tests cover both modes. ADR: drop Autofac.

### Stage 6 — Logging
- [x] 6.1 Add Serilog via `AddSerilog`, configured from appsettings:
  - console sink
  - file sink `logFiles/myapp.log`: rolls on size, 10 MB, 6 files retained (log4net's 5 backups plus the active file)
  - log-context and trace/span enrichment

  Test: the sink configuration. ADR: logging.
- [x] 6.2 Add request logging, which replaces `Application_BeginRequest` and the `requestinfo`/`activityid` properties, and source-generated `LoggerMessage` methods. Test: the request event is captured with a trace ID.

### Stage 7 — HTTP endpoints (Minimal APIs)
- [x] 7.1 API conventions:
  - endpoint groups and `TypedResults`
  - PascalCase JSON
  - ProblemDetails and an exception handler
  - built-in validation
  - OpenAPI document plus a committed snapshot test

  ADRs: Minimal APIs vs controllers; error contract.
- [x] 7.2 Brands, drop-in compatible:
  - `GET /api/brands`, ordered by Id
  - `GET /api/brands/{id}`, where a non-integer ID still gets 400
  - `DELETE /api/brands/{id}` as a no-op

  Tests compare against the golden exchanges, and the deliberate deltas are asserted explicitly and recorded.
- [x] 7.3 Retire `/api/files` with a `410 Gone` ProblemDetails response that points to `/api/brands`. ADR and test.
- [x] 7.4 Picture endpoint `GET /items/{catalogItemId:int}/pic`, keeping the legacy route name:
  - root directory from `Catalog:PicturesPath`, resolved at startup
  - traversal-safe lookup through `PhysicalFileProvider`
  - content type from `FileExtensionContentTypeProvider`
  - range processing
  - 404 for a missing file (legacy returned 500)

  ADR: security fixes made during the migration.
- [x] 7.5 Catalog reads, with `PictureUri` built by `LinkGenerator`:
  - `GET /api/items` (validated `pageSize` 1–100 and `pageIndex` ≥ 0)
  - `GET /api/items/{id}`
  - `GET /api/types`
- [x] 7.6 `POST /api/items` → 201 + Location. It enforces the legacy validation rules (culture-invariant price check), and `PictureFileName` is not client-writable: a new item gets the legacy default picture `dummy.png`. As the first endpoint that reads a request body, it also sets the body size limit (legacy: 4 MB, from `httpRuntime`; see ADR-0009).
- [x] 7.7 `PUT /api/items/{id}` and `DELETE /api/items/{id}`.

### Stage 8 — Async verification
- [x] 8.1 Sweep for sync-over-async and synchronous I/O on request paths, the log sinks included (ADR-0018). Verify that cancellation flows from `RequestAborted` to EF Core. Add cancellation tests and update the async ADR.

### Stage 9 — OpenAPI docs & Swagger UI
- [x] 9.1 Swagger UI at `/swagger` over `/openapi/v1.json` (Development only). ADR: OpenAPI tooling.
- [x] 9.2 Documentation completeness: summaries, tags and XML comments. Documented error responses match the runtime `application/problem+json`. Snapshot updated.

### Stage 10 — Test consolidation
- [x] 10.1 Collect code coverage and publish a report. Close the real gaps.
- [x] 10.2 Add a Docker trait and a documented Docker-free subset of the tests. CI publishes test results and coverage. Complete the README testing section.

### Stage 11 — Cutover & cleanup
- [x] 11.1 Tag `legacy-final` as the rollback point. ADR: cutover and rollback, including databases adopted with the Stage 4.3 baseline (ADR-0012: rollback by pointing the legacy app at the same database, which holds while migrations stay expand-only).
- [x] 11.2 Move the pictures into the API project and switch the default `PicturesPath`.
- [x] 11.3 Delete the legacy projects, `eShopLegacyMVC.sln`, the stop-files in the legacy project folders and all legacy-only assets. `docs/legacy` stays as a reference, with its capture tool and that tool's stop-files. ADR summarizing the removals.
- [x] 11.4 Package hygiene, dead-code removal, and a final pass on the README, this plan and `DECISIONS.md`.

### Stage 12 — Post-migration: write-endpoint authorization
- [ ] 12.1 Add JWT bearer auth with a `catalog:write` scope policy on item POST/PUT/DELETE. Tokens for local use come from `dotnet user-jwts`. Call `UseAuthentication` and `UseAuthorization` after the request logging, so that rejected requests are logged too (ADR-0019). Add the OpenAPI security scheme and tests (401/403/2xx). Reads and `/api/brands` stay anonymous. ADR.

---

## Working conventions

- **Before every commit:**
  - `dotnet build eShop.Catalog.slnx` and `dotnet test --solution eShop.Catalog.slnx` pass. From Stage 4.2, Docker must be running for the integration tests.
  - If the commit touches repo-wide build files, `dotnet build docs/legacy/capture/capture.cs` also passes.
- **Commits:** an imperative subject of at most 72 characters, and a body that explains what changed and why. One logical change per commit.
- **No commented-out legacy code.** Git history keeps it, and substantive removals are recorded in `DECISIONS.md`.

## Verification (end to end)

- `dotnet run --project src/eShop.Catalog.Api`, then check `/health/live`, `/health/ready`, `/swagger`, and each endpoint against the golden exchanges in `docs/legacy/contract`.
- Run again with `Catalog__UseMockData=true` and no database.
