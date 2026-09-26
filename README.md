# eShop Legacy → .NET 10

This project is based on Microsoft's [eShopModernizing](https://github.com/dotnet-architecture/eShopModernizing) sample. This fork keeps only the `eShopLegacyMVC` app. Its Web API layer is the starting point for an independent .NET Framework → .NET 10 modernization. The work is API-focused and does not cover the Razor/MVC UI.

## Repository layout

```
eShopLegacyMVC.sln
src/
  eShopLegacyMVC/          ASP.NET Web API 2 + MVC 5 app (.NET Framework 4.7.2)
  eShopLegacy.Utilities/   Shared class library (.NET Framework 4.6.1)
```

## Legacy baseline (as-is)

- **Web framework:** ASP.NET Web API 2 (`System.Web.Http`) + ASP.NET MVC 5 on .NET Framework 4.7.2, hosted in IIS / IIS Express
- **Data access:** Entity Framework 6 (SQL Server)
- **Dependency injection:** Autofac (`Autofac.Mvc5`, `Autofac.WebApi2`)
- **Logging:** log4net
- **Configuration:** `Web.config` / `ConfigurationManager`

## Planned modernization

| Area | Legacy (today) | Target |
|---|---|---|
| Framework | .NET Framework 4.7.2 (ASP.NET Web API 2 / MVC 5) | .NET 10 / ASP.NET Core |
| ORM | Entity Framework 6 | EF Core |
| Dependency injection | Autofac | Built-in `IServiceCollection` |
| Logging | log4net | `ILogger<T>` |
| Configuration | `Web.config` | `appsettings.json` + `IOptions<T>` |
| Data access | Synchronous | `async`/`await` |
| API docs | None | Swagger / OpenAPI |
| Tests | None | xUnit + `WebApplicationFactory` |

## Building the baseline

The legacy app needs Windows and Visual Studio with the **ASP.NET and web development** workload. You also need the **.NET Framework 4.6.1 targeting pack** for `eShopLegacy.Utilities`. The workload doesn't install it, and the build fails with `MSB3644` without it. Open `eShopLegacyMVC.sln` and run the app with IIS Express.

By default the app uses SQL Server LocalDB (`(localdb)\MSSQLLocalDB`). To run it without a database, set `UseMockData` to `true` in `Web.config`.

## License

MIT. See [LICENSE](LICENSE). The original code is © .NET Foundation and Contributors.
