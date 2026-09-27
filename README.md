# eShop Legacy → .NET 10

This project is based on Microsoft's [eShopModernizing](https://github.com/dotnet-architecture/eShopModernizing) sample. This fork keeps only the `eShopLegacyMVC` app. Its Web API layer is the starting point for an independent .NET Framework → .NET 10 modernization. The work is API-focused and does not cover the Razor/MVC UI.

## Migration status

The plan and its checklist are in [MIGRATION_PLAN.md](MIGRATION_PLAN.md). The checklist is the source of truth for progress; this table summarizes it.

| Stage | Status |
|---|---|
| 0 — Plan | Done |
| 1 — Baseline audit | Done |
| 2 — Scaffolding | In progress |
| 3 — Configuration | Not started |
| 4 — Domain & EF Core | Not started |
| 5 — Application services & DI | Not started |
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
eShop.Catalog.slnx           New solution (.NET 10, built with the dotnet CLI)
eShopLegacyMVC.sln           Legacy solution (built with MSBuild until cutover)
global.json                  .NET SDK and test runner selection
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

The app listens on `http://localhost:5043`. `GET /health/live` returns `200 Healthy` while the process is up. It does not check the database or any other dependency.

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
