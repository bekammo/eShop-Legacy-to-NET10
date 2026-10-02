using eShop.Catalog.Api.Brands;
using eShop.Catalog.Api.Catalog;
using eShop.Catalog.Api.Files;
using eShop.Catalog.Api.Health;
using eShop.Catalog.Api.Http;
using eShop.Catalog.Api.Items;
using eShop.Catalog.Api.Logging;
using eShop.Catalog.Api.Pictures;
using eShop.Catalog.Api.Types;
using Serilog.Debugging;

var builder = WebApplication.CreateBuilder(args);

// In every environment, not only in Development (ADR-0017): no scoped service is resolved from the root provider,
// and every registration is checked when the container is built.
builder.Host.UseDefaultServiceProvider(static options =>
{
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});

// Serilog reports its own failures, such as a log file that it cannot open, on the standard error output (ADR-0018).
SelfLog.Enable(Console.Error);
builder.Services.AddCatalogLogging();
builder.Services.AddCatalogHttp();
builder.Services.AddHealthChecks();
builder.Services.AddCatalogServices(builder.Configuration);
builder.Services.AddCatalogPictures();

var app = builder.Build();

app.UseCatalogRequestLogging();
app.UseCatalogErrorHandling();

// The OpenAPI document, /openapi/v1.json, in every environment (ADR-0020).
app.MapOpenApi();
app.MapHealthCheckEndpoints();
app.MapBrandEndpoints();
app.MapFileEndpoints();
app.MapPictureEndpoints();
app.MapItemEndpoints();
app.MapTypeEndpoints();

await app.RunAsync();
