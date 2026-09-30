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
| 5 — Application services & DI | In progress |
| 6 — Logging | Not started |
| 7 — HTTP endpoints | Not started |
| 8 — Async verification | Not started |
| 9 — OpenAPI docs & Swagger UI | Not started |
| 10 — Test consolidation | Not started |
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

## Repository layout

```
.github/workflows/ci.yml     CI: build, tests, vulnerable-package check
eShop.Catalog.slnx           New solution (.NET 10, built with the dotnet CLI)
eShopLegacyMVC.sln           Legacy solution (built with MSBuild until cutover)
global.json                  .NET SDK and test runner selection
dotnet-tools.json            Local .NET tools (dotnet-ef)
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
  legacy/                    Characterization data and the tool that captures it
src/
  eShop.Catalog.Api/         The new ASP.NET Core API (.NET 10)
  eShopLegacyMVC/            ASP.NET Web API 2 + MVC 5 app (.NET Framework 4.7.2)
  eShopLegacy.Utilities/     Shared class library (.NET Framework 4.6.1)
tests/
  eShop.Catalog.Api.UnitTests/         Tests without a host (xUnit v3)
  eShop.Catalog.Api.IntegrationTests/  Tests against the in-memory host and SQL Server in a container (xUnit v3)
  Shared/                              Test code both projects compile, such as the readers of docs/legacy
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
| `GET /health/ready` | `200 Healthy` when the API can reach its database and the database has every migration, otherwise `503 Unhealthy` ([ADR-0013](DECISIONS.md#adr-0013-seeding-migrate-on-startup-and-readiness)). |

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

The host does not start without a connection string, or with an invalid setting. To point Development at another SQL Server, override the connection string with user secrets, which are stored in your user profile, outside the repository:

```bash
dotnet user-secrets set "ConnectionStrings:CatalogDb" "<connection string>" --project src/eShop.Catalog.Api
```

## Local database

In Development the API needs a SQL Server of its own. There are two options, and in both the app creates, migrates and seeds the database `eShopCatalog` when it starts ([ADR-0014](DECISIONS.md#adr-0014-local-development-databases)).

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
