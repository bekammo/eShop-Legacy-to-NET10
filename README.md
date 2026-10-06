# eShop Legacy → .NET 10

[![CI](https://github.com/bekammo/eShop-Legacy-to-NET10/actions/workflows/ci.yml/badge.svg)](https://github.com/bekammo/eShop-Legacy-to-NET10/actions/workflows/ci.yml)

This project is based on Microsoft's [eShopModernizing](https://github.com/dotnet-architecture/eShopModernizing) sample. This fork keeps only the `eShopLegacyMVC` app. Its Web API layer is the starting point for an independent .NET Framework → .NET 10 modernization. The work is API-focused and does not cover the Razor/MVC UI.

## Migration status

The plan and its checklist are in [MIGRATION_PLAN.md](MIGRATION_PLAN.md). The checklist is the source of truth for progress; this table summarizes it.

| Stage | Status |
|---|---|
| 0 — Plan | Done |
| 1 — Baseline audit | Done |
| 2 — Scaffolding | Done |
| 3 — Configuration | Done |
| 4 — Domain & EF Core | Done |
| 5 — Application services & DI | Done |
| 6 — Logging | Done |
| 7 — HTTP endpoints | Done |
| 8 — Async verification | Done |
| 9 — OpenAPI docs & Swagger UI | Done |
| 10 — Test consolidation | In progress |
| 11 — Cutover & cleanup | Not started |
| 12 — Write-endpoint authorization | Not started |

## Documentation

| Document | Contents |
|---|---|
| [MIGRATION_PLAN.md](MIGRATION_PLAN.md) | Scope, architecture direction and the staged checklist. |
| [DECISIONS.md](DECISIONS.md) | Architecture decision records (ADRs). |
| [docs/legacy-audit.md](docs/legacy-audit.md) | Audit of the legacy code: pipeline, packages, endpoints, schema, configuration, logging, defects and risks, build and tests. |
| [docs/legacy/README.md](docs/legacy/README.md) | Characterization of the running legacy app: schema, seed data, golden HTTP exchanges, defect evidence, and the rules for comparing the new API against them. |
| [docs/behavior-changes.md](docs/behavior-changes.md) | Register of every deliberate difference between the new API and the legacy app. |
| [docs/openapi/v1.json](docs/openapi/v1.json) | The OpenAPI document of the new API, as the API serves it. A test keeps it current, so every change to the contract of an endpoint shows in its diff. |

## Repository layout

```
.github/workflows/ci.yml     CI: build, tests, vulnerable-package check
eShop.Catalog.slnx           New solution (.NET 10, built with the dotnet CLI)
eShopLegacyMVC.sln           Legacy solution (built with MSBuild until cutover)
global.json                  .NET SDK and test runner selection
dotnet-tools.json            Local .NET tools (dotnet-ef, ReportGenerator)
compose.yaml                 SQL Server in a container, for local development
Directory.Build.props        Build settings for the new solution
Directory.Packages.props     Central package versions for the new solution
nuget.config                 Package sources (both solutions)
.editorconfig                Code style and analyzer severities for the new solution
MIGRATION_PLAN.md
DECISIONS.md
docs/
  legacy-audit.md
  behavior-changes.md
  openapi/                   The OpenAPI document of the new API
  legacy/                    Characterization data and the tool that captures it
src/
  eShop.Catalog.Api/         The new ASP.NET Core API (.NET 10)
  eShopLegacyMVC/            ASP.NET Web API 2 + MVC 5 app (.NET Framework 4.7.2)
  eShopLegacy.Utilities/     Shared class library (.NET Framework 4.6.1)
tests/
  eShop.Catalog.Api.UnitTests/         Tests without a host (xUnit v3)
  eShop.Catalog.Api.IntegrationTests/  Tests against the in-memory host and SQL Server in a container (xUnit v3)
  Shared/                              Test code both projects compile, such as the readers of docs/legacy
  testconfig.json                      Test platform settings both projects use: code coverage
```

The build files at the repository root reach every project below them. Three folders opt out with stop-files (`Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`): the two legacy project folders and the Stage 1.2 capture tool in `docs/legacy/capture` ([ADR-0006](DECISIONS.md#adr-0006-solution-structure-and-build-conventions)).

## Legacy baseline (as-is)

- **Web framework:** ASP.NET Web API 2 (`System.Web.Http`) + ASP.NET MVC 5 on .NET Framework 4.7.2, hosted in IIS / IIS Express
- **Data access:** Entity Framework 6 (SQL Server)
- **Dependency injection:** Autofac (`Autofac.Mvc5`, `Autofac.WebApi2`)
- **Logging:** log4net
- **Configuration:** `Web.config` / `ConfigurationManager`

## Planned modernization

| Area | Legacy (today) | Target |
|---|---|---|
| Framework | .NET Framework 4.7.2 (ASP.NET Web API 2 / MVC 5) | .NET 10 / ASP.NET Core Minimal APIs |
| ORM | Entity Framework 6 | EF Core 10 with code-first migrations |
| Dependency injection | Autofac | Built-in `IServiceCollection` |
| Logging | log4net | Serilog behind `ILogger<T>` |
| Configuration | `Web.config` | `appsettings.json` + `IOptions<T>` |
| Data access | Synchronous | `async`/`await` |
| API docs | None | Built-in OpenAPI + Swagger UI |
| Tests | None | xUnit v3 + `WebApplicationFactory` + Testcontainers SQL Server |

## Building the new API

The new API needs a [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0): 10.0.100 or a later 10.0 feature band ([global.json](global.json)). It builds on Windows, Linux and macOS. Two solutions coexist until cutover, so always name the solution:

```bash
dotnet build eShop.Catalog.slnx
```

```bash
dotnet run --project src/eShop.Catalog.Api --launch-profile http
```

The app listens on `http://localhost:5043`. In Development it first creates or updates its database and seeds a new one with the sample items (see [Database migrations](#database-migrations)), so its database server must be available: LocalDB on Windows by default, or SQL Server in a container on any OS (see [Local database](#local-database)).

| Endpoint | Answers |
|---|---|
| `GET /health/live` | `200 Healthy` while the process is up. It does not check the database or any other dependency. |
| `GET /health/ready` | `200 Healthy` when the API can reach its database and the database has every migration, otherwise `503 Unhealthy` ([ADR-0013](DECISIONS.md#adr-0013-seeding-migrate-on-startup-and-readiness)). In mock mode it checks nothing and answers `200 Healthy`. |
| `GET /openapi/v1.json` | The OpenAPI 3.1 document of the API, in every environment ([ADR-0020](DECISIONS.md#adr-0020-minimal-api-endpoints-and-the-openapi-document)). It describes each operation, and the problem body of each error that it lists ([ADR-0029](DECISIONS.md#adr-0029-describing-the-api-in-the-openapi-document)). |
| `GET /swagger` | Swagger UI over that document, in Development only. Visual Studio and `dotnet watch` open it ([ADR-0028](DECISIONS.md#adr-0028-openapi-tooling-swagger-ui-in-development)). |
| `GET /api/brands` | Every brand, in ID order: `[{"Id":1,"Brand":"Azure"}, ...]`. |
| `GET /api/brands/{id}` | One brand, or 404. An ID that is not an integer is a 400. |
| `DELETE /api/brands/{id}` | Deletes nothing, as in the legacy app: 200 for a brand that exists, 404 otherwise. |
| `GET /api/files` | Retired: `410 Gone`, with a problem that points to `GET /api/brands`. The legacy app returned the brands there as a BinaryFormatter payload ([ADR-0022](DECISIONS.md#adr-0022-get-apifiles-retired-with-410-gone)). |
| `GET /items/{id}/pic` | The picture of an item, from the folder that `Catalog:PicturesPath` names: 400 for an ID below 1, 404 for an unknown item or a picture that is not in the folder. A `Range` request gets the part it asks for (206) ([ADR-0023](DECISIONS.md#adr-0023-security-fixes-made-during-the-migration)). |
| `GET /api/items?pageSize=10&pageIndex=0` | One page of items, in ID order: `ActualPage`, `ItemsPerPage`, `TotalItems`, `TotalPages` and `Data`. `pageSize` is 1–100 and `pageIndex` 0 or more; anything else is a 400 ([ADR-0024](DECISIONS.md#adr-0024-item-and-type-reads)). |
| `GET /api/items/{id}` | One item, with its brand, its type and the URL of its picture, or 404. An ID that is not an integer is a 400. |
| `POST /api/items` | Creates an item from a JSON body: 201, with its location and the item. A field that breaks its rule, or an unknown brand or type, is a 400 that names it ([ADR-0025](DECISIONS.md#adr-0025-creating-items)). Request bodies are limited to 4 MB. |
| `PUT /api/items/{id}` | Replaces the item's fields, with the rules of the create: 204, 404 for an unknown item ([ADR-0026](DECISIONS.md#adr-0026-updating-and-deleting-items)). |
| `DELETE /api/items/{id}` | Deletes the item: 204, or 404 for an unknown item. |
| `GET /api/types` | Every item type, in ID order. |

The brand and picture endpoints are drop-in compatible with the legacy app ([ADR-0002](DECISIONS.md#adr-0002-wire-contract-policy)). Their few deliberate differences, such as no XML, and the changes to the legacy item rules are in the [behavior-change register](docs/behavior-changes.md).

The catalog endpoints answer with JSON, with property names in PascalCase as the legacy Web API wrote them, whatever the `Accept` header asks for. Every error, except the plain-text answers of the health checks, is a problem details object ([RFC 9457](https://www.rfc-editor.org/rfc/rfc9457)), as `application/problem+json`, a route that matches nothing included. Its `traceId` holds the trace ID of the request, which finds the request's events in the log ([ADR-0021](DECISIONS.md#adr-0021-error-contract-problem-details)):

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Not Found",
  "status": 404,
  "traceId": "00-0af7651916cd43dd8448eb211c80319c-2e19c0fd39ec0d42-01"
}
```

To run without any database, turn on mock mode, which serves the catalog from memory, starting with the legacy sample data, and loses every change when the app stops ([ADR-0017](DECISIONS.md#adr-0017-built-in-dependency-injection-and-mock-mode)). In Bash:

```bash
Catalog__UseMockData=true dotnet run --project src/eShop.Catalog.Api --launch-profile http
```

## Configuration

Settings follow the ASP.NET Core defaults ([ADR-0009](DECISIONS.md#adr-0009-configuration)). Each source overrides the ones before it:

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. user secrets, in Development only
4. environment variables
5. the command line

.NET 10 also reads `eShop.Catalog.Api.settings.json` files between the second and third sources, but this project does not use them. No committed file holds a credential or a value for a deployed environment.

| Setting | Development | Other environments |
|---|---|---|
| `ConnectionStrings:CatalogDb` | LocalDB, database `eShopCatalog` (`appsettings.Development.json`), unless user secrets override it (see [Local database](#local-database)) | Required, for example as the environment variable `ConnectionStrings__CatalogDb` |
| `Database:MigrateOnStartup` | `true`: the migrations are applied, and a new database seeded, before the app accepts requests | `false`. The host refuses to start with `true` outside Development. |
| `Catalog:UseMockData` | `false` (`appsettings.json`). `true` serves the catalog from memory, and the two settings above are not read. | `false`, as in Development |
| `Catalog:PicturesPath` | `../eShopLegacyMVC/Pics`, the legacy app's pictures, relative to the content root (`appsettings.json`) | The same, until Stage 11.2 moves the pictures into the API. A published app needs it set, for example as `Catalog__PicturesPath`. The host does not start when the folder does not exist. |

The host does not start with an invalid setting, or without a connection string unless mock mode is on. To point Development at another SQL Server, override the connection string with user secrets, which are stored in your user profile, outside the repository:

```bash
dotnet user-secrets set "ConnectionStrings:CatalogDb" "<connection string>" --project src/eShop.Catalog.Api
```

## Logging

The API logs through Serilog, which the `Serilog` section of `appsettings.json` configures ([ADR-0018](DECISIONS.md#adr-0018-logging-with-serilog)). It logs events at Information and above, except ASP.NET Core's own events and the SQL of EF Core's commands, which it logs only from Warning. It writes them to two places:

- **The console**, one line per event, written on a background thread, so that a slow console holds up requests only once 10,000 events are waiting ([ADR-0027](DECISIONS.md#adr-0027-asynchronous-request-paths-and-cancellation)).
- **A log file**, `logFiles/myapp.log` under the content root. With `dotnet run` that is `src/eShop.Catalog.Api/logFiles/`, which git ignores. Each line is one event as JSON, in the [compact log event format](https://clef-json.org) (CLEF), with the trace and span IDs in `@tr` and `@sp`.

Each request is logged once, when it completes, for example as `HTTP GET /no-such-route responded 404 in 0.2975 ms`, with the query string and the user agent as properties of the event ([ADR-0019](DECISIONS.md#adr-0019-request-logging-and-application-log-events)). Every event of a request carries the request's trace ID, which a caller that sends a W3C `traceparent` header shares. Requests to the health checks are logged at Debug, so they do not show at the default level, but a failing check is still logged, at Error. In mock mode the app logs a warning when it starts.

The file rolls at 10 MiB (10,485,760 bytes, log4net's `10MB`), and the 6 newest files are kept, as with log4net in the legacy app. The first file is `myapp.log`, then come `myapp_001.log`, `myapp_002.log` and so on. The file with the highest number is the current one.

Like any setting, these can be overridden. For example, in Bash on Linux or macOS, to write the file elsewhere (its folder must be writable):

```bash
Serilog__WriteTo__File__Args__path=/tmp/eshop-catalog/myapp.log dotnet run --project src/eShop.Catalog.Api --launch-profile http
```

Or to see the SQL that EF Core runs (Bash cannot set a variable whose name holds dots, so it goes on the command line):

```bash
dotnet run --project src/eShop.Catalog.Api --launch-profile http -- --Serilog:MinimumLevel:Override:Microsoft.EntityFrameworkCore.Database.Command=Information
```

## Local database

In Development the API needs a SQL Server of its own, unless mock mode is on. There are two options, and in both the app creates, migrates and seeds the database `eShopCatalog` when it starts ([ADR-0014](DECISIONS.md#adr-0014-local-development-databases)).

| | LocalDB (default) | SQL Server in a container |
|---|---|---|
| Runs on | Windows | Windows, Linux or macOS, with Docker |
| Server | The installed SQL Server Express LocalDB | [`compose.yaml`](compose.yaml), with the image the integration tests run |
| Setup | None: `appsettings.Development.json` points at it | A password in `.env` and a connection string in user secrets |
| Start over | Drop the database | `docker compose down --volumes` |

### LocalDB

LocalDB comes with Visual Studio (the *SQL Server Express LocalDB* component) and with the SQL Server Express installer. `appsettings.Development.json` points at its default instance, `(localdb)\MSSQLLocalDB`, which starts on the first connection, so there is nothing to set up. To check that it is installed:

```bash
sqllocaldb info MSSQLLocalDB
```

To start over with the sample items, stop the app and drop the database, for example with sqlcmd:

```bash
sqlcmd -S "(localdb)\MSSQLLocalDB" -Q "DROP DATABASE eShopCatalog"
```

### SQL Server in a container

`compose.yaml` runs SQL Server only. The API still runs with `dotnet run`. SQL Server needs at least 2 GB of memory. There is no Arm64 image. A Mac with Apple silicon can run the amd64 image under Docker Desktop's Rosetta emulation, but this setup has not been tested here, and other Arm64 machines may not run it at all.

1. Choose a password for the `sa` login and put it in a file named `.env` next to `compose.yaml`. Git ignores that file. SQL Server requires at least 8 characters, from at least three of these groups: upper-case letters, lower-case letters, digits and symbols. Use upper-case and lower-case letters with digits only. That meets the policy, and it avoids symbols such as `$`, `#`, `!`, `;` and quotes, which `.env` files, shells or connection strings treat specially.

   ```text
   MSSQL_SA_PASSWORD=<password>
   ```

2. Start the server. `--wait` returns once it accepts logins. The databases are kept in the Docker volume `eshop-catalog_sqlserver-data`.

   ```bash
   docker compose up --detach --wait
   ```

3. Point the API at it with user secrets, using the same password:

   ```bash
   dotnet user-secrets set ConnectionStrings:CatalogDb 'Data Source=127.0.0.1,1433;Initial Catalog=eShopCatalog;User ID=sa;Password=<password>;TrustServerCertificate=True' --project src/eShop.Catalog.Api
   ```

4. Run the API as usual. It creates, migrates and seeds `eShopCatalog` on that server.

The server accepts connections from `127.0.0.1` only. If port 1433 is taken, for example by a SQL Server installed on the machine, add `CATALOG_DB_PORT=<port>` to `.env` and use that port in the connection string. The connection is encrypted, but the server's certificate is self-signed, so the connection string trusts it (`TrustServerCertificate=True`). That is acceptable only for a server on your own machine.

| To | Run |
|---|---|
| Stop the server and keep the databases | `docker compose stop`, or `docker compose down`, which also removes the container |
| Start over without databases | `docker compose down --volumes` |
| See why the server did not start | `docker compose logs sqlserver` |
| Go back to LocalDB | `dotnet user-secrets remove "ConnectionStrings:CatalogDb" --project src/eShop.Catalog.Api` |

SQL Server sets the `sa` password only on its first start, when it creates its system databases in the empty volume. If you later change it in `.env`, the server keeps the old one, its health check fails, and `docker compose up --wait` reports it unhealthy. Change the password back, change it on the server too (`ALTER LOGIN sa WITH PASSWORD = ...`, logged in with the old one), or start over with `docker compose down --volumes`. If SQL Server rejects the password as too weak, the container exits at once, and `docker compose logs sqlserver` shows why.

## Database migrations

The schema is created and changed by EF Core migrations in `src/eShop.Catalog.Api/Data/Migrations` ([ADR-0011](DECISIONS.md#adr-0011-ef-core-migration-strategy)). The `dotnet-ef` tool is pinned in [dotnet-tools.json](dotnet-tools.json). Restore it once per clone:

```bash
dotnet tool restore
```

The `dotnet ef` commands need the solution restored, so build it once first (`dotnet build eShop.Catalog.slnx`). After a model change, add a migration. A unit test fails while the model has changes that no migration captures.

```bash
dotnet ef migrations add <Name> --project src/eShop.Catalog.Api --output-dir Data/Migrations
```

In Development the app applies the migrations when it starts (`Database:MigrateOnStartup`). To create or update a database without starting the app, for example the LocalDB one (for the container, use the connection string from [SQL Server in a container](#sql-server-in-a-container)):

```bash
dotnet ef database update --project src/eShop.Catalog.Api --connection "Data Source=(localdb)\MSSQLLocalDB;Initial Catalog=eShopCatalog;Integrated Security=True"
```

Both ways also seed the legacy app's 12 sample items, with IDs 1–12, into a database that nobody has used yet: one without items, whose item-ID sequence has never handed out a value, and which is not an adopted legacy database. They seed it once. To start again with the sample items, drop the database. The brands and types are reference data in the migrations themselves ([ADR-0013](DECISIONS.md#adr-0013-seeding-migrate-on-startup-and-readiness)).

Write the SQL that brings an empty database, or one these migrations created, up to date, for review before a deployment applies it. It holds the brands and types but no sample items. The `artifacts` folder is ignored by git.

```bash
dotnet ef migrations script --idempotent --project src/eShop.Catalog.Api --output artifacts/migrate.sql
```

The tool does not start the app: it builds the `DbContext` through a design-time factory, so these commands need no configuration. Only `database update` connects. `dotnet ef migrations remove` tries to check the database first, so remove an unapplied migration with `--force`.

A database that the legacy app created cannot take the migrations until [`docs/legacy/baseline.sql`](docs/legacy/baseline.sql) has adopted it. The procedure is in [docs/legacy/README.md](docs/legacy/README.md#adopting-an-existing-legacy-database) ([ADR-0012](DECISIONS.md#adr-0012-adopting-a-legacy-database)).

## Running the tests

The tests use xUnit v3 on Microsoft.Testing.Platform, which `global.json` selects for `dotnet test` ([ADR-0007](DECISIONS.md#adr-0007-test-strategy)). The unit tests need only the SDK. The integration tests start SQL Server in a container, so Docker must be running. The first run pulls the pinned image `mcr.microsoft.com/mssql/server:2025-CU9-ubuntu-24.04`, which `compose.yaml` also runs for [local development](#sql-server-in-a-container). Each test class, or each test that needs one, gets a database of its own in that container.

Run every test:

```bash
dotnet test --solution eShop.Catalog.slnx
```

Run one test project, for example only the unit tests:

```bash
dotnet test --project tests/eShop.Catalog.Api.UnitTests
```

Write a TRX report per test project into `TestResults/`:

```bash
dotnet test --solution eShop.Catalog.slnx --report-xunit-trx --results-directory TestResults
```

Measure the code coverage of the API ([ADR-0030](DECISIONS.md#adr-0030-code-coverage)). Each run writes a Cobertura file per test project, under a new name, and the report below merges every file in the folder, so start from an empty one. In Bash:

```bash
rm -rf TestResults/coverage
```

```bash
dotnet test --solution eShop.Catalog.slnx --coverage --results-directory TestResults/coverage
```

Then merge the files into one HTML report, `TestResults/coverage/report/index.html`, with ReportGenerator, a local tool pinned in [dotnet-tools.json](dotnet-tools.json) (`dotnet tool restore` installs it):

```bash
dotnet reportgenerator -reports:"TestResults/coverage/*.cobertura.xml" -targetdir:TestResults/coverage/report
```

Coverage counts the code of `eShop.Catalog.Api` only. It leaves out the test projects, and the code that source generators add, whose source paths lie under `obj/`, such as the `[LoggerMessage]` methods.

When a change alters the contract of an endpoint, `OpenApiDocumentTests.Document_matches_the_committed_snapshot` fails until [docs/openapi/v1.json](docs/openapi/v1.json) matches the document that the API serves. The test writes that document to `OpenApi/v1.received.json` in the integration tests' output folder, and its failure message gives the path. If the change is intended, copy that file over `docs/openapi/v1.json`, and review the diff with the code ([ADR-0020](DECISIONS.md#adr-0020-minimal-api-endpoints-and-the-openapi-document)).

## Continuous integration

[`.github/workflows/ci.yml`](.github/workflows/ci.yml) runs on every pull request, on every push to `main` and once a week ([ADR-0008](DECISIONS.md#adr-0008-continuous-integration)). It has two jobs:

- **Build and test.** Builds the new solution in Release with warnings as errors, builds the Stage 1.2 capture tool, runs every test, and uploads the TRX reports as the `test-results` artifact.
- **Vulnerable packages.** Fails when any direct or transitive package has a known vulnerability, at any severity, or when the vulnerability data cannot be fetched. An advisory accepted with `NuGetAuditSuppress` does not fail it.

The legacy solution is not built in CI. It needs Windows and Visual Studio, so it is built locally whenever a repo-wide build file changes.

GitHub disables the weekly run after 60 days without repository activity. Re-enable it from the Actions tab.

## Building the baseline

The legacy app needs Windows and Visual Studio with the **ASP.NET and web development** workload. You also need the **.NET Framework 4.6.1 targeting pack** for `eShopLegacy.Utilities`. The workload doesn't install it, and the build fails with `MSB3644` without it. Open `eShopLegacyMVC.sln` and run the app with IIS Express.

To build from the command line, use the MSBuild that comes with Visual Studio. `dotnet build` fails with `MSB4019`, because the .NET SDK does not ship `Microsoft.WebApplication.targets`.

```bash
MSBuild.exe eShopLegacyMVC.sln -restore -p:Configuration=Debug
```

The Debug and Release builds succeed with MSBuild 18.10 (Visual Studio 2026), with 6 warnings. The warnings are listed in the [audit](docs/legacy-audit.md#8-build-tooling-and-tests).

By default the app uses SQL Server LocalDB (`(localdb)\MSSQLLocalDB`) and creates the database `Microsoft.eShopOnContainers.Services.CatalogDb` on first use. The database name must stay as it is, because the sequence scripts hard-code it. To run the app without a database, set `UseMockData` to `true` in `Web.config`.

## License

MIT. See [LICENSE](LICENSE). The original code is © .NET Foundation and Contributors.
