# Legacy audit: eShopLegacyMVC

This is the Stage 1.1 audit of the legacy app, as imported at commit `80278f1`. It covers:

- `src/eShopLegacyMVC`: ASP.NET Web API 2 + MVC 5 on .NET Framework 4.7.2
- `src/eShopLegacy.Utilities`: .NET Framework 4.6.1

It is the reference for the later stages of [MIGRATION_PLAN.md](../MIGRATION_PLAN.md). The legacy code itself stays untouched.

## Contents

1. [Scope and method](#1-scope-and-method)
2. [Architecture and request pipeline](#2-architecture-and-request-pipeline)
3. [Package inventory](#3-package-inventory)
4. [Endpoint inventory](#4-endpoint-inventory)
5. [EF6 data model and expected schema](#5-ef6-data-model-and-expected-schema)
6. [Configuration and logging](#6-configuration-and-logging)
7. [Defects and risks](#7-defects-and-risks)
8. [Build, tooling and tests](#8-build-tooling-and-tests)
9. [Corrections to the migration plan](#9-corrections-to-the-migration-plan)
10. [Runtime confirmation](#10-runtime-confirmation)

## 1. Scope and method

- **Sources read:** every source, view, configuration and project file, and the resolved NuGet graph (`obj/project.assets.json`, `obj/*.nuget.g.props/targets`).
- **How:** section by section. Each defect claim was then re-checked against the code and against framework behaviour: MVC 5.2.7, Web API 5.2.7, EF 6.2.0, IIS Express integrated pipeline, log4net 2.0.10.
- **Citations:** claims cite `path:line` relative to `src/eShopLegacyMVC/` unless another root is given.
- **Build:** the legacy solution was built with MSBuild ([section 8](#8-build-tooling-and-tests)).
- **Runtime:** runtime behaviour (status codes, bodies, the real schema) is captured separately in Stage 1.2.

## 2. Architecture and request pipeline

One IIS-hosted ASP.NET 4.7.2 application:

- Web API 2 and MVC 5 share a single System.Web integrated pipeline and one `RouteTable`.
- The layering is thin: controllers → `ICatalogService` → `CatalogDBContext` (EF6), with an in-memory `CatalogServiceMock` as the alternative implementation.

### 2.1 Components

| Component | Location | Responsibility | Lifetime | Fate |
|---|---|---|---|---|
| `MvcApplication` | `Global.asax.cs:21-93` | Startup, per-request log4net properties, `Session_Start` | HttpApplication pool | `Program.cs` (2.1, 5.3, 6.2) |
| `WebApiConfig`, `RouteConfig`, `FilterConfig`, `BundleConfig` | `App_Start/*.cs` | Routes, global `HandleErrorAttribute`, UI bundles | Startup | Endpoint groups (7.x); bundles dropped |
| `ApplicationModule` | `Modules/ApplicationModule.cs:8-40` | Autofac service registrations | Startup | `AddCatalogServices` (5.3) |
| `BrandsController`, `FilesController` | `Controllers/WebApi/*.cs` | `/api/brands`, `/api/files` | Per request (Web API scope) | Drop-in (7.2) / 410 (7.3) |
| `PicController` | `Controllers/PicController.cs:9-90` | `GET items/{catalogItemId:int}/pic` | Per request (MVC scope) | Picture endpoint (7.4) |
| `CatalogController` | `Controllers/CatalogController.cs:10-164` | Razor CRUD UI | Per request (MVC scope), disposed twice | Re-exposed as `/api/items` (7.5–7.7) |
| `CatalogController2` | `Controllers/Api/CatalogController.cs:5-13` | Nothing: never discovered | – | Deleted (11.4) |
| `ICatalogService` / `CatalogService` | `Services/*.cs` | Seven synchronous operations; `IDisposable` | Per scope (DB mode) | Async service (5.1) |
| `CatalogServiceMock` | `Services/CatalogServiceMock.cs` | List-based CRUD over `PreconfiguredData` | Singleton (mock mode) | Thread-safe in-memory service (5.2) |
| `CatalogDBContext`, `CatalogDBInitializer`, `CatalogItemHiLoGenerator`, `PreconfiguredData` | `Models/**` | EF6 model, create + seed, item IDs, seed data | Per scope / root / singleton / static | EF Core model, migrations, `UseHiLo`, seeding (4.x) |
| `PaginatedItemsViewModel` | `ViewModel/PaginatedItemsViewModel.cs` | Paging result for the MVC list | Per request | `PaginatedItems<T>` with guards (5.1) |
| Razor views | `Views/**` | UI; the layout footer reads `Session` | Per request | Dropped |
| `Serializing` | `src/eShopLegacy.Utilities/Serializing.cs` | BinaryFormatter (de)serialization | Per call | Removed at cutover |

### 2.2 Startup (`Application_Start`, `Global.asax.cs:27-36`)

1. **Before `Application_Start`.** Pre-start code in Autofac.Integration.Mvc, System.Web.Optimization and WebPages registers dynamic modules: Autofac's `RequestLifetimeHttpModule`, which ends the MVC request scope at `EndRequest`, and the bundling module. log4net configures itself on the first `LogManager.GetLogger` call, from `[assembly: XmlConfigurator(ConfigFile = "log4net.xml")]` (`Properties/AssemblyInfo.cs:37`).
2. **`RegisterContainer()` (`:60-81`).** Scans MVC and Web API controllers, then registers `ApplicationModule`. `UseMockData` is read with `bool.Parse` (`:68`). Two separate resolvers are set: MVC (`AutofacDependencyResolver`) and Web API (`AutofacWebApiDependencyResolver`).
3. **Routes and filters.**
   - `GlobalConfiguration.Configure(WebApiConfig.Register)` adds the Web API attribute routes (there are none) and `DefaultApi` `api/{controller}/{id}`.
   - `AreaRegistration.RegisterAllAreas()` is a no-op: there are no areas.
   - The global `HandleErrorAttribute` is registered.
   - MVC routes: attribute routes (only the picture route), `IgnoreRoute("{resource}.axd/{*pathInfo}")`, then `Default` `{controller}/{action}/{id}` → `Catalog/Index`.
   - Bundles are registered.
4. **`ConfigDataBase()` (`:83-91`).** In DB mode only, it resolves `CatalogDBInitializer` from the **root** container and installs it with `Database.SetInitializer`. It reads `UseMockData` again.

   The database is created and seeded lazily, inside the first request that uses a `DbContext`. Because the initializer comes from the root container, the `CatalogItemHiLoGenerator` singleton is shared between seeding and every request-scoped `CatalogService`.

### 2.3 Request pipeline

Handlers:

- `ExtensionlessUrlHandler-Integrated-4.0` (`Web.config:100`) sends extensionless URLs to managed routing. It is registered for `GET`, `HEAD`, `POST`, `DEBUG`, `PUT`, `DELETE`, `PATCH` and `OPTIONS`.
- The `system.web/httpModules` entries and the two ISAPI handlers are classic-pipeline only. They are dead here.

Modules added by the app:

- `TelemetryCorrelationHttpModule` (starts an `Activity`).
- `ApplicationInsightsWebTracking` (no instrumentation key, see [6.5](#65-application-insights)).
- `SessionStateModuleAsync`, which replaces the built-in session module (`Web.config:87-92`).

Route evaluation order and what each URL shape reaches:

| Order | Route | Template | Served by |
|---|---|---|---|
| 1 | Web API attribute routes | (none declared) | – |
| 2 | `DefaultApi` | `api/{controller}/{id}` | `BrandsController`, `FilesController`. Any other `api/{x}[/{y}]` gets a Web API 404 body. |
| 3 | MVC attribute route `GetPicRouteTemplate` | `items/{catalogItemId:int}/pic` | `PicController.Index` |
| 4 | Ignore | `{resource}.axd/{*pathInfo}` | IIS handlers |
| 5 | `Default` | `{controller}/{action}/{id}`, defaults `Catalog/Index` | `CatalogController`. `/api` and unknown controllers throw an HttpException 404. |

- A last segment with a dot (for example `/api/brands/1.5`) never reaches routing. IIS maps it to the static file handler.
- Static assets (`Content`, `Scripts`, `fonts`, `Images`, `Pics`) are served by IIS. The site root is the project folder.

The request flow differs between the two frameworks:

- **Web API** (for example `GET /api/brands`):
  - The request does not take session state.
  - The dependency scope is created per `HttpRequestMessage` and disposed after the response.
  - `BrandsController.Get()` returns the `DbSet` itself, so the SQL query runs during serialization.
  - Responses carry `Cache-Control: no-cache`, `Pragma: no-cache` and `Expires: -1`.
- **MVC** (for example `GET /Catalog/Details/1` or `/items/1/pic`):
  - `MvcHandler` requires session state, so every MVC request takes an exclusive InProc session lock and a new client gets an `ASP.NET_SessionId` cookie. `Session_Start` stores `MachineName` and `SessionStartTime`. Only the layout footer reads them (`Views/Shared/_Layout.cshtml:35`).
  - The Autofac request scope lives in `HttpContext.Items` and ends at `EndRequest`.

### 2.4 Dependency injection (Autofac 6.1.0)

| Registration | Implementation | Lifetime | Notes |
|---|---|---|---|
| `RegisterControllers` (`Global.asax.cs:65`) | `CatalogController`, `PicController` | Per dependency, MVC request scope | Disposed by MVC `ReleaseController` **and** by the scope. `CatalogController.Dispose` then disposes the injected service and, through it, the `DbContext`: container-owned instances are disposed several times. |
| `RegisterApiControllers` (`:66`) | `BrandsController`, `FilesController` | Per dependency, Web API request scope | – |
| `ICatalogService` (`ApplicationModule.cs:18-29`) | `CatalogServiceMock` / `CatalogService` | Singleton (mock) / per scope (DB) | Chosen by `UseMockData` |
| `CatalogDBContext` (`:31-32`) | – | Per scope | Registered in both modes, used only in DB mode |
| `CatalogDBInitializer` (`:34-35`) | – | Per scope, but resolved from the root | Effectively an AppDomain singleton |
| `CatalogItemHiLoGenerator` (`:37-38`) | – | Singleton | Shared by the initializer and all services |

The plan calls this "four plain registrations". The port also has to replace:

- controller activation for two frameworks
- two request-scope mechanisms
- the root-resolved initializer

### 2.5 Error handling

There is no `<customErrors>`, so the default `RemoteOnly` applies. What a client sees depends on the framework, the failure, and whether the request is local:

| Failure | Local request (localhost) | Remote request |
|---|---|---|
| Exception in an MVC action, filter or view | ASP.NET error page ("yellow screen") with exception and stack trace, 500 | `Views/Shared/Error.cshtml` through `HandleErrorAttribute`, 500 |
| `HttpNotFound()` / `HttpStatusCodeResult(400)` in MVC | IIS detailed error page 404.0 / 400.0 | IIS generic error page |
| Unknown MVC controller or action | ASP.NET "The resource cannot be found." page, 404 | Generic ASP.NET error page, 404 |
| Web API binding error or unknown controller | 400/404/405 body `{"Message", "MessageDetail"}` | Same status, `Message` only |
| Unhandled exception in Web API | 500 body with `ExceptionMessage`, `ExceptionType`, `StackTrace` | 500 `{"Message":"An error has occurred."}` |

No code path logs an exception:

- There is no `Application_Error`.
- There is no Web API `IExceptionLogger` or exception filter.
- `HandleErrorAttribute` does not log.

## 3. Package inventory

### 3.1 How the project restores

- **Restore style.** `eShopLegacyMVC.csproj` restores as a **PackageReference** project (`obj/eShopLegacyMVC.csproj.nuget.g.props`). It has 48 direct `PackageReference` items and a `ProjectReference` to `eShopLegacy.Utilities`, and resolves 55 packages in total.
- **Stale `packages.config`.** `src/eShopLegacyMVC/packages.config` is ignored, and it disagrees with the csproj: Autofac 4.9.1 against 6.1.0, and it lacks `autofac.webapi2`, the four `Microsoft.AspNet.WebApi*` packages and `System.Net.Http`.
- **Dead package-folder paths.** 30 `<Reference>` items carry `HintPath`s into `..\..\packages\`, and 7 `Exists()`-guarded `<Import>` items point into it. That folder does not exist. The build works because the generated `nuget.g.props`/`targets` files and the NuGet asset resolution supply the same assemblies from the global package cache, and the guarded imports are silently skipped.
- **Compiler.** `Microsoft.Net.Compilers` 2.10.0 replaces the compiler that ships with MSBuild. The app compiles with csc 2.10, C# 7.3.
- **Client libraries.** 7 client-side packages deliver no files under PackageReference. The UI serves the committed files instead, for example `Scripts/jquery-3.3.1.js` although the csproj declares jQuery 3.5.0. Two Modernizr versions match the `modernizr-*` bundle.

### 3.2 Direct packages

The plan asks for a split into runtime, UI-only and build-only. Three more categories are needed to describe all 48 packages honestly:

| Category | Count | Meaning |
|---|---|---|
| Runtime | 15 | On the API, data, logging or DI path |
| Runtime, inert telemetry | 8 | Loaded, but Application Insights has no key |
| Superseded by the framework | 3 | The build binds to the .NET Framework assembly instead |
| Unused | 7 | Nothing references them, but they are copied to `bin` |
| UI-only | 13 | Razor, bundling, client libraries, session |
| Build-only | 2 | Compilers |

| Package | Version | Category | Used by | .NET 10 fate |
|---|---|---|---|---|
| Autofac | 6.1.0 | Runtime (DI) | `Global.asax.cs:60-81`, `ApplicationModule.cs` | Built-in DI |
| autofac.webapi2 | 6.0.1 | Runtime (DI) | `Global.asax.cs:66, 77-78` | Built-in DI |
| Autofac.Mvc5 | 4.0.2 | Runtime (DI, incl. `PicController`) | `Global.asax.cs:65, 74` | Built-in DI |
| EntityFramework | 6.2.0 | Runtime (data) | `Models/**`, `Services/CatalogService.cs` | EF Core 10 (`Microsoft.EntityFrameworkCore.SqlServer`) |
| Microsoft.AspNet.WebApi (meta) / .Core / .WebHost / .Client | 5.2.7 | Runtime | `WebApiConfig.cs`, `Controllers/WebApi/*`, content negotiation | ASP.NET Core Minimal APIs; XML dropped |
| Microsoft.AspNet.Mvc | 5.2.7 | Runtime (picture route) + UI | `PicController`, `CatalogController`, routes, filters | Picture endpoint in ASP.NET Core; UI dropped |
| Newtonsoft.Json | 12.0.1 | Runtime (indirect: Web API formatters) | No direct code use | `System.Text.Json` with PascalCase kept |
| log4net | 2.0.10 | Runtime (logging) | `Global.asax.cs`, `CatalogController`, `PicController`, `log4Net.xml` | Serilog behind `ILogger<T>` |
| Microsoft.Web.Infrastructure | 1.0.0.0 | Runtime (support) | Dynamic module registration | Dropped |
| System.Diagnostics.DiagnosticSource, System.Runtime.CompilerServices.Unsafe, System.Threading.Tasks.Extensions | 4.5.1, 4.5.0, 4.5.1 | Runtime (support) | Autofac, Application Insights | Dropped (in-box) |
| Microsoft.ApplicationInsights (+ .Agent.Intercept, .DependencyCollector, .PerfCounterCollector, .Web, .WindowsServer, .WindowsServer.TelemetryChannel) | 2.9.1 (2.4.0) | Runtime, inert | `Web.config:89-90`, `ApplicationInsights.config` | Dropped |
| Microsoft.AspNet.TelemetryCorrelation | 1.0.5 | Runtime, inert | `Web.config:87-88` | Dropped (built-in `Activity`) |
| System.IO.Compression, System.IO.Compression.ZipFile, System.Net.Http | 4.3.0, 4.3.0, 4.3.4 | Superseded by the framework | CSV/zip seed, `HttpResponseMessage` | Dropped |
| Pipelines.Sockets.Unofficial, System.IO.Pipelines, System.Threading.Channels, System.Memory, System.Buffers, System.Numerics.Vectors, System.Diagnostics.PerformanceCounter | various | Unused | Nothing | Dropped |
| Microsoft.AspNet.Razor, Microsoft.AspNet.WebPages, Microsoft.AspNet.Web.Optimization, WebGrease, Antlr | 3.2.7, 3.2.7, 1.1.3, 1.6.0, 3.5.0.2 | UI-only | Views, anti-forgery, bundling | Dropped |
| Microsoft.AspNet.SessionState.SessionStateModule | 1.1.0 | UI-only | `Web.config:91-92`, layout footer | Dropped |
| bootstrap, jQuery, jQuery.Validation, Microsoft.jQuery.Unobtrusive.Validation, Modernizr, popper.js, Respond | 4.3.1, 3.5.0, 1.19.4, 3.2.11, 2.8.3, 1.14.3, 1.4.2 | UI-only (no files delivered) | Committed `Scripts/`, `Content/` files | Dropped |
| Microsoft.CodeDom.Providers.DotNetCompilerPlatform | 2.0.1 | Build-only (runtime view compilation) | `Web.config:113-118` | Dropped |
| Microsoft.Net.Compilers | 2.10.0 | Build-only | Compiler override | Dropped |

### 3.3 Version conflicts

- **Autofac.Mvc5 4.0.2** supports Autofac ≥ 4.0.1 and < 5.0.0, but runs against Autofac 6.1.0 (NU1608). It works only through the `Autofac` binding redirect (`Web.config:54-57`), outside its supported range.
- **Missing redirect.** No binding redirect exists for `System.Net.Http.Formatting`. `Autofac.Integration.WebApi` references 5.2.0.0, and `bin` has 5.2.7.0 (MSB3247).
- **Downgrades.** `System.Diagnostics.DiagnosticSource` 4.5.1 and `System.Threading.Tasks.Extensions` 4.5.1 are pinned below what Autofac 6.1.0 asks for (NU1605). The `DiagnosticSource` redirect sends Autofac's 4.0.5.0 reference down to 4.0.3.1.
- **Advisories.** NuGet audit flags Newtonsoft.Json 12.0.1 (NU1903, high, GHSA-5crp-9r3c-p9vr) and log4net 2.0.10 (NU1902, moderate, GHSA-4f7c-pmjv-c25w). Audit runs on direct packages only, and it checks declared versions, not the committed client-side files.

### 3.4 eShopLegacy.Utilities

- **What it is:** a classic class library targeting .NET Framework 4.6.1, with one class. `Serializing.SerializeBinary`/`DeserializeBinary` wrap `BinaryFormatter`.
- **Who uses it:** only `FilesController` uses it. `DeserializeBinary` is never called. `BrandsController` imports the namespace without using it.
- **.NET 10:** `BinaryFormatter` throws on .NET 9 and later, so the project cannot be ported as it is. It goes away with `/api/files` (plan 7.3, 11.3).

## 4. Endpoint inventory

### 4.1 Web API 2 (`api/{controller}/{id}`, verbs by method-name convention)

| Method | Route | Action | Success | Errors | Notes |
|---|---|---|---|---|---|
| GET | `/api/brands` | `BrandsController.Get()` | 200, array of `{Id, Brand}` | – | Returns the `DbSet` (deferred). JSON or XML by content negotiation. |
| GET | `/api/brands/{id}` | `BrandsController.Get(int id)` | 200, `{Id, Brand}` | 404, empty body; 400 when `id` does not bind to `int` | Loads all brands and filters in memory (`BrandsController.cs:31-32`). |
| DELETE | `/api/brands/{id}` | `BrandsController.Delete(int id)` | 200, empty body | 404, empty body; 400 when `id` does not bind | Deletes nothing: "demo only" (`:48`). |
| GET | `/api/files` | `FilesController.Get()` | 200, BinaryFormatter stream of `List<BrandDTO{Id, Brand}>` | – | `StreamContent` without a `Content-Type`. Retired in 7.3. |

Content negotiation uses the Web API 2 default formatters:

- JSON through Newtonsoft with default settings, so properties are **PascalCase**.
- XML through `DataContractSerializer`, namespace `http://schemas.datacontract.org/2004/07/eShopLegacyMVC.Models`.
- A browser `Accept` header that lists `application/xml` gets XML.
- With no `Accept` header, or one that nothing matches, the response is JSON.

### 4.2 MVC 5

| Method | Route | Action | Success | Errors |
|---|---|---|---|---|
| GET | `/items/{catalogItemId:int}/pic` (name `GetPicRouteTemplate`) | `PicController.Index` | 200, file bytes; MIME type from a switch on the extension | 400 for `id ≤ 0`; 404 for an unknown item; 500 when the file is missing; a non-integer ID falls through to `Default` and gives 404 |
| GET | `/`, `/Catalog`, `/Catalog/Index?pageSize=10&pageIndex=0` | `Index` | 200 HTML, items ordered by `Id` | 500 for `pageSize=0` and for negative values |
| GET | `/Catalog/Details/{id}` | `Details(int?)` | 200 HTML | 400 when `id` is missing, 404 when the item is unknown |
| GET / POST | `/Catalog/Create` | `Create()` / `Create([Bind] CatalogItem)` | 302 → `/` | 200 with the form and validation messages |
| GET / POST | `/Catalog/Edit/{id}` | `Edit(int?)` / `Edit([Bind] CatalogItem)` | 302 → `/` | 400/404 on GET; 200 with the form on invalid input |
| GET / POST | `/Catalog/Delete/{id}` | `Delete(int?)` / `DeleteConfirmed(int)` | 302 → `/` | 400/404 on GET; 500 when POSTing an unknown ID |

- All POSTs carry `[ValidateAntiForgeryToken]`, so only a browser that loaded the form can post.
- `PictureUri` is built with `Url.RouteUrl("GetPicRouteTemplate", ...)` (`CatalogController.cs:160-163`).

### 4.3 Unreachable routes

- **`CatalogController2`** declares `[Route("api")]` in `Controllers/Api/CatalogController.cs`. MVC discovers controllers only by the `Controller` name suffix, and Autofac's `RegisterControllers` uses the same filter, so the route is never registered. It is not an `ApiController` either.
- **`GET /api`** matches no Web API route (`{controller}` is required). It falls through to the MVC `Default` route as controller `api`, and gives 404.
- **No attribute routes.** `MapHttpAttributeRoutes` and `MapMvcAttributeRoutes` are called, but only `PicController` declares an attribute route.

### 4.4 Business rules behind the UI

The re-exposed item endpoints (7.5–7.7) need these rules.

**Paging** (`CatalogService.cs:21-35`):

- Items are ordered by `Id`, with `Skip(pageSize * pageIndex).Take(pageSize)`.
- `TotalItems` comes from `LongCount()`, and `TotalPages = ceil(count / pageSize)`.
- Defaults are `pageSize = 10` and `pageIndex = 0`. Nothing is validated.
- Brand and type are included with each item.

**Create** (`CatalogController.cs:60-74`, `CatalogService.cs:51-56`):

- `[Bind(Include = "Id,Name,Description,Price,PictureFileName,CatalogTypeId,CatalogBrandId,AvailableStock,RestockThreshold,MaxStockThreshold,OnReorder")]`
- The form posts only `Name`, `Description`, `CatalogBrandId`, `CatalogTypeId`, `Price` and the three stock fields.
- `PictureFileName` defaults to `dummy.png` (the `CatalogItem` constructor).
- `Id` is always replaced by the next HiLo value.

**Validation** (`Models/CatalogItem.cs`):

- `Name` is `[Required]`. The 50-character limit comes only from the EF6 mapping, so it is enforced at `SaveChanges`.
- `Price`:
  - `[RegularExpression(@"^\d+(\.\d{0,2})*$")]`, applied to the price formatted in the current culture
  - `[Range(0, 1000000)]`, the `int` overload, which rounds the decimal (see [D9](#7-defects-and-risks))
- `AvailableStock`, `RestockThreshold` and `MaxStockThreshold` are `[Range(0, 10000000)]`.
- Value-type fields are implicitly required.
- Brand and type existence is enforced only by the foreign keys.

**Edit** (`CatalogService.cs:58-62`):

- The bound object is attached with `EntityState.Modified`, so every column is written, including the ones the client did not send.
- The Edit form shows `PictureFileName` read-only but still posts it. It has no `OnReorder` field.

**Delete** (`CatalogService.cs:64-68`): a hard delete of the item row.

**Types and brands:** full lists from `GetCatalogTypes()` and `GetCatalogBrands()`. They are used for the dropdowns and for `/api/brands`.

## 5. EF6 data model and expected schema

`CatalogDBContext` (`Models/CatalogDBContext.cs`) uses the connection string `name=CatalogDBContext`, with fluent configuration on top of EF6 conventions. EF6 6.2.0 with `CreateDatabaseIfNotExists` should produce the schema below. The sequences come from the raw scripts run in `Seed`.

### 5.1 Tables

| Table | Column | Type | Null | Key / generation | Source |
|---|---|---|---|---|---|
| `dbo.Catalog` | `Id` | `int` | no | PK `PK_dbo.Catalog`, **not** identity (`DatabaseGeneratedOption.None`), values from HiLo | `CatalogDBContext.cs:61-65` |
| | `Name` | `nvarchar(50)` | no | | `:67-69` |
| | `Description` | `nvarchar(max)` | yes | | convention |
| | `Price` | `decimal(18,2)` | no | | convention (EF6 default precision) |
| | `PictureFileName` | `nvarchar(max)` | no | | `:74-75` |
| | `CatalogTypeId` | `int` | no | FK → `CatalogType`, index `IX_CatalogTypeId` | `:83-85` |
| | `CatalogBrandId` | `int` | no | FK → `CatalogBrand`, index `IX_CatalogBrandId` | `:79-81` |
| | `AvailableStock`, `RestockThreshold`, `MaxStockThreshold` | `int` | no | | convention |
| | `OnReorder` | `bit` | no | | convention |
| `dbo.CatalogBrand` | `Id` | `int` | no | PK `PK_dbo.CatalogBrand`, `IDENTITY(1,1)` | convention |
| | `Brand` | `nvarchar(100)` | no | | `:52-54` |
| `dbo.CatalogType` | `Id` | `int` | no | PK `PK_dbo.CatalogType`, `IDENTITY(1,1)` | convention |
| | `Type` | `nvarchar(100)` | no | | `:38-40` |
| `dbo.__MigrationHistory` | `MigrationId` `nvarchar(150)`, `ContextKey` `nvarchar(300)`, `Model` `varbinary(max)`, `ProductVersion` `nvarchar(32)` | | no | PK (`MigrationId`, `ContextKey`) | Written by `CreateDatabaseIfNotExists`: the compressed model, used for the compatibility check on later starts |

`PictureUri` is ignored by the mapping (`:77`). It is a computed URL, not data.

### 5.2 Constraints

- `FK_dbo.Catalog_dbo.CatalogBrand_CatalogBrandId` and `FK_dbo.Catalog_dbo.CatalogType_CatalogTypeId` are both **`ON DELETE CASCADE`**. These are required relationships, so EF6's cascade convention applies. Deleting a brand or a type would delete its items. No code deletes brands or types: the brand delete is a no-op.
- There are no other indexes, unique constraints, defaults, views, procedures or triggers.

### 5.3 Sequences and HiLo

| Sequence | Script | Type | Start | Increment | Used by |
|---|---|---|---|---|---|
| `catalog_hilo` | `Models/Infrastructure/dbo.catalog_hilo.Sequence.sql` | `bigint` | 1 | 10 | `CatalogItemHiLoGenerator` (item IDs) |
| `catalog_brand_hilo` | `dbo.catalog_brand_hilo.Sequence.sql` | `bigint` | 1 | 10 | One `NEXT VALUE` during seeding; the result is discarded |
| `catalog_type_hilo` | `dbo.catalog_type_hilo.Sequence.sql` | `bigint` | 1 | 10 | One `NEXT VALUE` during seeding; the result is discarded |

- **Hard-coded database name.** All three scripts start with `USE [Microsoft.eShopOnContainers.Services.CatalogDb]`. They only work when the connection string names exactly that database ([D12](#7-defects-and-risks)).
- **How `CatalogItemHiLoGenerator` works** (`Models/CatalogItemHiLoGenerator.cs`):
  - It takes `NEXT VALUE FOR catalog_hilo` and hands out that value and the next nine, under a lock.
  - The block size (10) is hard-coded. It does not come from the sequence.
  - The `bigint` value is cast to `int` without a check.
  - The block lives in memory, so a restart skips the unused rest of the block.
- **Seeding** takes two blocks (1–10 and 11–20) for the 12 items. `catalog_hilo` then stands at 11, and the next created item gets ID 13.
- **Comparison with EF Core `UseHiLo("catalog_hilo")`:**
  - Block allocation is the same: `NEXT VALUE`, then the value plus the next increment − 1 values.
  - EF Core takes the block size from the sequence increment in the model.
  - EF Core generates a key only when it has the default value. A client-supplied `Id` would be kept. The legacy code always overwrites it.

### 5.4 Seeding (`Models/Infrastructure/CatalogDBInitializer.cs:32-43`)

1. **Order:** the three sequence scripts, then the types, then the brands, then the items.
2. **Brands and types.** The code assigns each one an `Id` from its sequence, but the columns are `IDENTITY`. EF6 does not send the value, so the database assigns 1–4 (types) and 1–5 (brands) in list order.
3. **Items.** The 12 items in `PreconfiguredData.cs:14-25` hard-code brand IDs 2 and 5 and type IDs 1–3. Their own hard-coded `Id` values are overwritten by HiLo.

Seed data (`UseCustomizationData=false`):

| Types | Brands |
|---|---|
| 1 Mug, 2 T-Shirt, 3 Sheet, 4 USB Memory Stick | 1 Azure, 2 .NET, 3 Visual Studio, 4 SQL Server, 5 Other |

Items 1–12:

- Pictures `1.png` … `12.png`.
- `AvailableStock` 100, other stock fields 0, `OnReorder` false.
- Prices 19.50, 8.50, 12.00 and similar.

**Seeding is not atomic.** Each step calls `SaveChanges` on its own. `CreateDatabaseIfNotExists` never seeds a database that already exists. So a failure part-way leaves a partly seeded database that the app will never repair.

**Customization mode** (`UseCustomizationData=true`, off in `Web.config:20`) works differently:

- It reads `Setup/*.csv`.
- It **deletes every file in `Pics`** and re-extracts `Setup/CatalogItems.zip`.

### 5.5 Queries

`CatalogService` issues these tracked queries:

- `LongCount` plus an ordered `Skip`/`Take` page with `Include` of brand and type.
- `FirstOrDefault` by ID with the same includes.
- The full `DbSet` for brands and types.

`MultipleActiveResultSets=True` is set, but nothing needs it: no query reads while another reader is open. The new database drops it (plan 3.1).

## 6. Configuration and logging

### 6.1 `Web.config`

| Element | What it does | Fate in the port |
|---|---|---|
| `connectionStrings/CatalogDBContext` | LocalDB, `Initial Catalog=Microsoft.eShopOnContainers.Services.CatalogDb`, `MultipleActiveResultSets=True` | `ConnectionStrings:CatalogDb`, own database `eShopCatalog`, MARS off (3.1) |
| `appSettings/UseMockData` | Mock or EF service; skips the initializer | `Catalog:UseMockData` (5.3) |
| `appSettings/UseCustomizationData` | CSV/zip seed | Dropped |
| `appSettings/webpages:*`, `ClientValidationEnabled`, `UnobtrusiveJavaScriptEnabled` | Razor and client validation | Dropped (UI) |
| `system.web/compilation debug="true"` | Debug compilation. Only `Web.Release.config` removes it, and only when publishing. | Dropped |
| `system.web/httpRuntime targetFramework="4.6.1"` | Runtime compatibility behaviour, default limits (4 MB request, 110 s) | Dropped; Kestrel limits set deliberately |
| `system.web/sessionState InProc` + `SessionStateModuleAsync` | Session for the layout footer | Dropped |
| `system.web/globalization culture="en-US"` | Culture of every request thread (see 6.3) | Replaced by explicit invariant parsing and validation |
| `system.web/httpModules`, the ISAPI handlers | Classic pipeline only | Dropped (dead) |
| `runtime/assemblyBinding` (10 redirects) | Version unification. The `System.Net.Http.Formatting` redirect is missing. | Dropped (no binding redirects on .NET 10) |
| `system.webServer/modules` (TelemetryCorrelation, AI, Session) | Pipeline modules | Dropped |
| `system.webServer/handlers` (Extensionless, all verbs) | Managed routing for extensionless URLs | Dropped (Kestrel) |
| `entityFramework/defaultConnectionFactory` | Never used: the context uses `name=` | Dropped (dead) |
| `entityFramework/providers` | EF6 SQL Server provider | `UseSqlServer` (EF Core) |
| `system.codedom` | Runtime compilation of views and `Global.asax` | Dropped |
| (absent) `customErrors`, authentication, `machineKey` | See 2.5 and [D3](#7-defects-and-risks) | ProblemDetails (7.1); JWT (Stage 12) |

`Views/Web.config`, `Web.Debug.config` (no active transform), `Web.Release.config` (removes `debug` only) and `ApplicationInsights.config` are UI or telemetry plumbing, and they are all dropped. The IIS Express settings in the csproj (`IISUrl` `http://localhost:52429/`, no SSL) become `launchSettings.json`.

### 6.2 appSettings consumers

| Key | Read at | Parsing | If missing or invalid |
|---|---|---|---|
| `UseMockData` | `Global.asax.cs:68`, `:85` | `bool.Parse`, twice | `Application_Start` throws before routes and resolvers are registered. The app does not start correctly. |
| `UseCustomizationData` | `CatalogDBInitializer.cs:29` (constructor, DB mode) | `bool.Parse` | Resolving the initializer throws. The custom initializer is not installed. |

### 6.3 Culture dependence (`en-US` pinned, `Web.config:38`)

| Behaviour | Culture used | Effect |
|---|---|---|
| Form binding of `Price` | Current culture (en-US) | `12.50` is accepted; `12,5` and `1,000.50` are rejected ("The value '…' is not valid for Price.") |
| `[RegularExpression]` on `Price` | Validates `Convert.ToString(Price, CurrentCulture)` | Works only because en-US uses `.`. Under de-DE every price with decimals would fail. Scale-sensitive: `12.500` is rejected. |
| `[Range(0, 1000000)]` on `Price` | `Convert.ToInt32`, invariant | Rounds half to even; throws `OverflowException` above `Int32.MaxValue` ([D9](#7-defects-and-risks)) |
| `[DataType(Currency)]` display | Current culture | `$19.50` in the UI |
| Query and route integers (`pageSize`, `id`) | Invariant | Culture-independent |
| CSV seed prices | Invariant | Dropped with the CSV seed |

The plan links the culture pin only to price validation. It also drives form binding and display. JSON input in the new API is culture-invariant, so the port needs explicit rules, not a culture setting.

### 6.4 Logging (log4net)

| Setting | Value | Behaviour |
|---|---|---|
| Configuration | `[assembly: XmlConfigurator(ConfigFile = "log4net.xml")]` | The file is `log4Net.xml`. It is found only because the file system is case-insensitive; on a case-sensitive file system log4net would log nothing, silently. |
| Root level | `ALL` | Every level, in every environment |
| Appender | `RollingFileAppender`, `logFiles\myapp.log` | Resolved against the app root, so the log sits inside the web root. Append mode, exclusive lock. |
| Rolling | `Size`, `maximumFileSize` 10MB, `maxSizeRollBackups` 5, `staticLogFileName` true | At most 6 files: `myapp.log` and `.1`–`.5` |
| Layout | `%date [%thread] %property{activity} %level %logger - %property{requestinfo}%newline%message%newline%newline` | Multi-line entries. `%property{activity}` never matches the `activityid` key the code sets, so it prints `(null)` and `ActivityIdHelper` never runs. `requestinfo` is `RawUrl + ", " + UserAgent`. |

Log call sites:

| Where | Level | Message |
|---|---|---|
| `Global.asax.cs:54` | DEBUG | `WebApplication_BeginRequest`, on every request |
| `CatalogController.cs:24, 33, 51, 79, 116` | INFO | `Now loading... /Catalog/<action>?...` (IDs and paging values) |
| `CatalogController.cs:64` | INFO | `Now processing... /Catalog/Create?catalogItemName={Name}`: free text, logged before validation |
| `CatalogController.cs:102, 136` | INFO | `Now processing... /Catalog/Edit?id=…` / `DeleteConfirmed?id=…` |
| `CatalogController.cs:144` | DEBUG | `Now disposing`, twice per request (see 2.4) |
| `PicController.cs:27` | INFO | `Now loading... /items/Index?{id}/pic` |

- Nothing logs at WARN, ERROR or FATAL, and no exception is ever logged.
- `BrandsController`, `FilesController`, the services and the models do not log at all.

### 6.5 Application Insights

- **Configuration.** There is no instrumentation key in the repository. `ApplicationInsights.config` only has the "add your key" comment.
- **Runtime.** The HTTP modules, plus the 11 telemetry modules and 13 telemetry initializers in `ApplicationInsights.config`, still load and run on every managed request. The server channel drops every item because it has no key. So the component is inert as far as data leaving the process goes, not in CPU.
- **Latent switch.** A host that sets `APPINSIGHTS_INSTRUMENTATIONKEY`, as Azure App Service can, would switch telemetry on without any code change.

## 7. Defects and risks

Severity is rated for the deployed legacy app. "Plan" says where the migration handles each item.

| ID | Severity | Finding | Evidence | Plan |
|---|---|---|---|---|
| D1 | **High** | **Arbitrary file read through the picture endpoint.** `PicController` reads `Path.Combine(Server.MapPath("~/Pics"), item.PictureFileName)`. `PictureFileName` is in the `[Bind]` list of Create and Edit, so any client can store `..\Web.config` or a rooted path such as `C:\…`. `Path.Combine` returns a rooted second argument as is. The file is then served to anyone, with no authentication. | `PicController.cs:38-46`; `CatalogController.cs:62, 100` | 7.4 (traversal-safe lookup), 7.6 (not client-writable) |
| D2 | Medium | **Overposting and lost updates.** `Id` and `PictureFileName` are bindable, although not on the form. Edit attaches the bound object with `EntityState.Modified`, so fields the client does not post are written as defaults. Every normal edit resets `OnReorder` to false (not on the form). A partial post nulls `Description`, zeroes the stock fields and resets the picture to `dummy.png`. | `CatalogController.cs:62, 100`; `CatalogService.cs:58-62`; `Views/Catalog/Edit.cshtml` | 5.1 (explicit field updates), 7.6–7.7 |
| D3 | Medium | **No authentication or authorization** on any endpoint, reads or writes. Anti-forgery tokens only stop cross-site posts. | `FilterConfig.cs:10`; no `authentication` in `Web.config` | Accepted risk until Stage 12 |
| D4 | Medium | **BinaryFormatter.** `/api/files` returns a BinaryFormatter stream, and `DeserializeBinary` exists, unused. A client that deserializes the payload is exposed to BinaryFormatter's known risks. | `FilesController.cs:21-36`; `eShopLegacy.Utilities/Serializing.cs` | 7.3 (410), 11.3 (project removed) |
| D5 | Medium | **Vulnerable packages:** Newtonsoft.Json 12.0.1 (NU1903, high) and log4net 2.0.10 (NU1902, moderate). The legacy app runs with them until Stage 11. | Build log, section 8 | Replaced (6.1, 7.1); legacy only |
| D6 | Medium | **Unvalidated paging.** `pageSize=0` divides by zero in `PaginatedItemsViewModel` (500). Negative `pageSize` or `pageIndex` is passed to EF6 `Skip`/`Take`, which throws (500). `pageSize * pageIndex` overflows unchecked. `pageSize` has no upper bound, so one request can read the whole table. | `PaginatedItemsViewModel.cs:23`; `CatalogService.cs:25-31` | 5.1, 7.5 (1–100, ≥ 0) |
| D7 | Low | **A missing picture file gives 500** (`FileNotFoundException`, not handled). | `PicController.cs:44` | 7.4 (404) |
| D8 | Low | **The MIME lookup is case-sensitive:** `.PNG` becomes `application/octet-stream`. | `PicController.cs:56-86` | 7.4 |
| D9 | Medium | **Price validation defects.** `[Range(0, 1000000)]` is the `int` overload: the decimal is rounded half to even first, so 1000000.50 passes and 1000000.51 fails. A price above `Int32.MaxValue` throws `OverflowException` during validation (500). The regular expression uses `*` where `?` was meant; this has no effect on formatted decimals. The regex is also scale-sensitive and culture-dependent (6.3). | `Models/CatalogItem.cs:22-24` | 7.6 (culture-invariant rules) |
| D10 | Medium | **Database constraints surface as 500s.** A name longer than 50 characters passes MVC validation and fails EF6 validation at `SaveChanges` (`DbEntityValidationException`). An unknown brand or type ID violates the foreign key (`SqlException`). | `CatalogItem.cs:16-17` vs `CatalogDBContext.cs:67-69`; `CatalogService.cs:51-56` | 7.6 |
| D11 | Medium | **Writes to unknown items give 500s.** Edit of an unknown ID updates zero rows (`DbUpdateConcurrencyException`). `DeleteConfirmed` passes `null` to `DbSet.Remove` (`ArgumentNullException`). | `CatalogService.cs:58-68`; `CatalogController.cs:134-139` | 7.7 |
| D12 | Medium | **Hard-coded database name** in the sequence scripts (`USE [Microsoft.eShopOnContainers.Services.CatalogDb]`). With any other `Initial Catalog`, the sequences land in the wrong database, or seeding fails. The new, empty database is then never seeded, because the initializer only seeds when it creates the database. | `Models/Infrastructure/*.Sequence.sql:1-2`; `CatalogDBInitializer.cs:333-337` | New database by migrations (4.2); baseline for existing databases (4.3) |
| D13 | Low | **Seeding is neither atomic nor re-runnable** (see 5.4). Customization mode deletes `Pics` before extracting the zip. | `CatalogDBInitializer.cs:32-43, 339-354` | 4.4 (idempotent seeder) |
| D14 | Low | **HiLo generator.** An unchecked `bigint` → `int` cast wraps silently past `Int32.MaxValue`. The block size is hard-coded, not read from the sequence. Failed inserts and restarts consume IDs (by design, but visible to clients). | `CatalogItemHiLoGenerator.cs:11-31` | 4.1 (`UseHiLo`) |
| D15 | Medium | **Mock mode is not thread-safe.** `CatalogServiceMock` is a singleton over a plain `List<T>`, mutated by concurrent requests. `ComposeCatalogItems` mutates the shared items. `Create` uses `Max(Id) + 1`, which throws on an empty list and reuses IDs after deletes. `First()` throws for an unknown brand or type. | `ApplicationModule.cs:18-23`; `CatalogServiceMock.cs:12-82` | 5.2 (thread-safe in-memory service) |
| D16 | Low | **Disposal of container-owned objects.** `CatalogController.Dispose` disposes the injected service and its `DbContext`, which Autofac also disposes. The controller itself is disposed twice per request. | `CatalogController.cs:142-150`; `CatalogService.cs:70-73` | 5.1 (service not `IDisposable`) |
| D17 | Low | **Inefficient brand lookup.** `GET /api/brands/{id}` and `DELETE` load every brand and filter in memory. `GET /api/brands` returns a deferred `DbSet`, so the query runs during serialization. | `BrandsController.cs:22-50`; `CatalogService.cs:46-49` | 5.1 (server-side lookup) |
| D18 | Low | **Logging defects.** The `activity` / `activityid` mismatch. The configuration file name only works on a case-insensitive file system. Root level `ALL` everywhere. No exception is ever logged. Unsanitized user input goes into the log (item name, raw URL, User-Agent), which allows forged lines. The log file sits inside the web root. | Section 6.4 | 6.1–6.2, 7.1 |
| D19 | Low | **Information disclosure.** `compilation debug="true"` is committed. There are no `customErrors`, so local requests get stack traces. `X-AspNet-Version`, `X-AspNetMvc-Version`, `X-Powered-By` and `Server` headers are sent. The layout footer shows the server's machine name. | `Web.config:31`; section 2.5; `_Layout.cshtml:35` | 7.1 (ProblemDetails; no stack headers) |
| D20 | Low | **Static exposure.** The site root is the project folder. Configuration and data files that IIS has a MIME mapping for are served as static files: `log4Net.xml`, `Setup/*.csv`, `Setup/CatalogItems.zip`. Found by reading the configuration; not verified at runtime. | `eShopLegacyMVC.csproj` Content items; IIS static handler | Dropped (the API serves only the pictures root) |
| D21 | Low | **Startup configuration is fragile:** `bool.Parse` with no fallback (6.2). | `Global.asax.cs:68, 85`; `CatalogDBInitializer.cs:29` | 3.1 (validated options, `ValidateOnStart`) |
| D22 | Low | **Two seed items cannot be edited through the UI.** Their names contain `<T` (`Cup<T> White Mug`, `Cup<T> Sheet`), which ASP.NET request validation rejects on post, so Edit returns 500. There is no `[AllowHtml]` or `ValidateInput(false)`. Found by reading the code; not verified at runtime. | `PreconfiguredData.cs:22, 24` | Not applicable to the JSON API; the new API must accept these names |
| D23 | Low | **Cascading foreign keys:** deleting a brand or type would delete its items (5.2). | `CatalogDBContext.cs:79-85` | 4.1 reproduces the schema; the brand delete stays a no-op |
| D24 | Low | **Session state on every MVC request.** The exclusive session lock serializes one browser's picture requests. The session exists only for the footer. | `Web.config:33, 91-92` | Dropped |
| D25 | Low | **Autofac.Mvc5 outside its supported range**, a missing `System.Net.Http.Formatting` redirect, and package downgrades (3.3). | Build warnings NU1608, MSB3247, NU1605 | Dropped with Autofac |
| D26 | Info | **Application Insights** loads and runs without sending anything. An environment variable would switch it on (6.5). | `Web.config:87-90` | Dropped |
| D27 | Info | **Dead code and configuration:** `CatalogController2`, `AreaRegistration.RegisterAllAreas()`, unused `using`s (`System.Runtime.Remoting.Messaging`, `eShopLegacy.Utilities` in `BrandsController`), `DeserializeBinary`, 7 unused packages, the stale `packages.config`, dead `HintPath`s and imports, `defaultConnectionFactory`, classic-mode modules and handlers, and the two brand and type sequences. | Sections 2–5 | Removed with the legacy projects (11.3–11.4) |

## 8. Build, tooling and tests

### 8.1 Build verification

The legacy solution builds from the command line:

```bash
MSBuild.exe eShopLegacyMVC.sln -restore -t:Rebuild -p:Configuration=Debug -m
```

| Configuration | MSBuild | Result | Warnings | Errors | Time |
|---|---|---|---|---|---|
| Debug | 18.10.1 (Visual Studio 2026, `C:\Program Files\Microsoft Visual Studio\18\Community`) | Build succeeded | 6 | 0 | 4–8 s |
| Release | 18.10.1 | Build succeeded | 6 | 0 | 5 s |

- `dotnet build` (SDK 10.0.401) fails with `MSB4019`. The web project imports `$(VSToolsPath)\WebApplications\Microsoft.WebApplication.targets`, and the .NET SDK does not ship it. Visual Studio's MSBuild is required.
- Output goes to `src/eShopLegacyMVC/bin/` (the web project) and `src/eShopLegacy.Utilities/bin/<Configuration>/`. Both are gitignored.

### 8.2 Build warnings

| Warning | Meaning | Matters for the migration? |
|---|---|---|
| NU1608 | `Autofac.Mvc5` 4.0.2 requires Autofac < 5.0.0; 6.1.0 was resolved | No. Autofac is dropped. It explains why the legacy DI works only through binding redirects. |
| NU1902 | log4net 2.0.10: moderate advisory GHSA-4f7c-pmjv-c25w | Legacy-only risk (D5). Serilog replaces it. |
| NU1903 | Newtonsoft.Json 12.0.1: high advisory GHSA-5crp-9r3c-p9vr | Legacy-only risk (D5). The new API uses `System.Text.Json`. |
| NU1605 (×2) | Downgrades of `System.Diagnostics.DiagnosticSource` (4.7.1 → 4.5.1) and `System.Threading.Tasks.Extensions` (4.5.2 → 4.5.1) below Autofac 6.1.0's requirements | No. These packages are in-box on .NET 10. |
| MSB3247 | Conflicting versions of `System.Net.Http.Formatting` (5.2.0.0 referenced, 5.2.7.0 deployed); no binding redirect | No. Recorded as D25. |

The new solution treats warnings as errors, except NuGet audit NU1901–NU1904 (plan 2.1).

### 8.3 Toolchain prerequisites

| Tool | Needed for | Notes |
|---|---|---|
| Windows | Everything legacy | .NET Framework and IIS Express |
| Visual Studio 2026 (MSBuild 18) with the ASP.NET and web development workload | Building the web project | Supplies `Microsoft.WebApplication.targets` |
| .NET Framework 4.7.2 and 4.6.1 targeting packs | `eShopLegacyMVC` and `eShopLegacy.Utilities` | The workload does not install 4.6.1; without it the build fails with `MSB3644` |
| NuGet (through `-restore`) | Package restore | PackageReference; no `packages` folder |
| IIS Express 10 | Running the app | `IISUrl` `http://localhost:52429/`, integrated pipeline, CLR v4.0 |
| SQL Server LocalDB (`MSSQLLocalDB`) | DB mode | The database name must be `Microsoft.eShopOnContainers.Services.CatalogDb` (D12) |

A run is not reproducible unless the database is fresh:

- `CreateDatabaseIfNotExists` never re-seeds an existing database.
- Every created item, and every restart, moves the HiLo sequence.

### 8.4 Tests

There are none:

- There is no test project in `eShopLegacyMVC.sln`.
- No file name contains `Test` or `Spec`.
- The tracked sources reference no test framework (xUnit, NUnit, MSTest, `Microsoft.NET.Test.Sdk`); the only matches are the ignore patterns in `.gitignore`.

The legacy behaviour is therefore pinned by the Stage 1.2 characterization, not by tests.

## 9. Corrections to the migration plan

The plan's "Key findings" were written before this audit. Where the code says otherwise, later stages should follow the audit:

1. **Autofac is more than four registrations** (2.4). The port replaces:
   - controller activation for two frameworks
   - per-request scopes for each
   - the root-resolved initializer that shares the HiLo generator
2. **No activity ID was ever logged** (6.4). Stage 6.2 replaces the `requestinfo` context (URL and User-Agent). The trace ID it adds is new behaviour, not parity.
3. **There are three sequences, not one**, plus EF6's `__MigrationHistory` (5.1, 5.3). The schema comparison in 4.2 and the baseline in 4.3 must account for them.
4. **The HiLo parity is in the block allocation only** (5.3). EF Core keeps a client-supplied key, so the API must not let clients set `Id` (7.6).
5. **The `en-US` pin affects more than price validation** (6.3): form binding and display too. The price rules in 7.6 must be defined without a culture.
6. **The package classification needs more categories** (3.2): unused, superseded by the framework, and inert telemetry.
7. **Error details depend on where the request comes from** (2.5). Local requests get exception details that remote clients never saw. The golden exchanges must be read with this in mind.
8. **The plan's defect list is incomplete.** Section 7 adds:
   - the price range rounding (D9)
   - 500s from database constraints and unknown items (D10, D11)
   - the hard-coded database name (D12)
   - the vulnerable packages (D5)
   - the missing exception logging and the other logging defects (D18)

## 10. Runtime confirmation

Stage 1.2 ran the legacy app against a fresh LocalDB database. The captured data is in [docs/legacy](legacy/README.md).

### 10.1 What the run confirmed

- **Schema.** [`schema.sql`](legacy/schema.sql) matches section 5 exactly: tables, types, nullability, identity columns, PK, FK and index names, cascades, and the three sequences. `__MigrationHistory` holds one row, `InitialCreate` with EF `6.2.0-61023`. It is not marked as a system object.
- **Seed data.** [`seed-data.json`](legacy/seed-data.json) matches 5.4. `catalog_hilo` is 11 after seeding.
- **Defects.** These were reproduced:

| Defect | Evidence |
|---|---|
| D1 | [`pic-path-traversal-relative`](legacy/evidence/pic-path-traversal-relative.json) serves `Global.asax`; [`pic-path-traversal-absolute`](legacy/evidence/pic-path-traversal-absolute.json) serves `C:\Windows\win.ini` |
| D2 | [`edit-overwrites-unposted-fields`](legacy/evidence/edit-overwrites-unposted-fields.json), [`create-ignores-posted-id`](legacy/evidence/create-ignores-posted-id.json) |
| D6 | [`catalog-reads`](legacy/evidence/catalog-reads.json): `DivideByZeroException`, EF6 `ArgumentException` for negative values and for the overflow |
| D7, D8 | [`pic-missing-file`](legacy/evidence/pic-missing-file.json), [`pic-extension-case`](legacy/evidence/pic-extension-case.json) |
| D9, D10 | [`create-item-validation`](legacy/evidence/create-item-validation.json): 1000000.50 accepted and 1000000.51 rejected; 500s for a 51-character name and for an unknown brand or type |
| D11 | [`unknown-item-writes`](legacy/evidence/unknown-item-writes.json) |
| D14 | [`hilo-restart-gap`](legacy/evidence/hilo-restart-gap.json): failed inserts consume IDs, and after a restart the next ID is 31 |
| D16, D18 | [`log4net-sample.log`](legacy/evidence/log4net-sample.log): `Now disposing` twice per request, and `(null)` in the activity column |

- **Not reproduced.** D20 (static exposure) and D22 (request validation on `Cup<T>` names) were not exercised. They remain findings from reading the code.

### 10.2 What only the run showed

- **`OPTIONS`.** `OPTIONS /api/brands` never reaches the app. IIS answers it: 200, `Allow: OPTIONS, TRACE, GET, HEAD, POST`.
- **`HEAD`.** Web API does not map `HEAD` to `GET`: `HEAD /api/brands` gives 405. `HEAD /items/1/pic` gives 404.
- **`/api/files`.** The BinaryFormatter stream (721 bytes) is labelled `Content-Type: text/html`, the ASP.NET default. `/api/files/1` returns the same payload.
- **Query-string ID.** `GET /api/brands?id=2` returns brand 2: Web API binds `id` from the query string too.
- **Dotted segment.** `/api/brands/1.5` is a 404 from the IIS static file handler. A non-integer ID without a dot gets the Web API 400.
- **Pictures.** Responses set an `ASP.NET_SessionId` cookie on a client's first request. `Range` headers are ignored: 200 with the full body.
- **Validation messages.** When a create attempt breaks two rules, which message the form shows varies between app starts. A post without the anti-forgery token gives 500 (`HttpAntiForgeryException`).
- **Local error detail.** The capture ran from localhost, so the error bodies and pages carry the local-only detail described in 2.5.
