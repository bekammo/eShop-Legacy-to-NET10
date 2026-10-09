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

builder.Host.UseDefaultServiceProvider(static options =>
{
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});

// Not debug code: Serilog reports its own failures, such as a log file that it cannot open, only to SelfLog.
SelfLog.Enable(Console.Error);
builder.Services.AddCatalogLogging();
builder.Services.AddCatalogHttp();
builder.Services.AddHealthChecks();
builder.Services.AddCatalogServices(builder.Configuration);
builder.Services.AddCatalogPictures();
builder.Services.AddCatalogAuthorization();

var app = builder.Build();

app.UseCatalogRequestLogging();

// After the request logging, so that the request event has the status that the error handling gives the client.
app.UseCatalogErrorHandling();

// Called explicitly, after routing and inside the request logging and error handling. WebApplication would
// otherwise add them before the app's middleware: a 401 or 403 would go unlogged, without a problem body,
// and authorization would not know the endpoint.
app.UseAuthentication();
app.UseAuthorization();

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
