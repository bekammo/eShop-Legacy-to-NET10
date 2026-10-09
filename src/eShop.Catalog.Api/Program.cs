using eShop.Catalog.Api.Authorization;
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
builder.Services.AddCatalogAuthorization();

var app = builder.Build();

app.UseCatalogRequestLogging();
app.UseCatalogErrorHandling();

// After the request logging, so that a request that they reject is logged too (ADR-0019), and inside the error
// handling, whose status code pages give their 401 and 403 a problem body (ADR-0021). After routing, so that
// authorization knows the endpoint and its policy (ADR-0034). WebApplication would otherwise add both before the app's
// middleware.
app.UseAuthentication();
app.UseAuthorization();

// The OpenAPI document, /openapi/v1.json, in every environment (ADR-0020), and Swagger UI over it, at /swagger, in
// Development only (ADR-0028).
app.MapOpenApi();
if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI(static options => options.SwaggerEndpoint("/openapi/v1.json", "v1"));
}

app.MapHealthCheckEndpoints();
app.MapBrandEndpoints();
app.MapFileEndpoints();
app.MapPictureEndpoints();
app.MapItemEndpoints();
app.MapTypeEndpoints();

await app.RunAsync();
