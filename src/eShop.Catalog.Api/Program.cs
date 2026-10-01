using eShop.Catalog.Api.Catalog;
using eShop.Catalog.Api.Health;
using eShop.Catalog.Api.Logging;
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
builder.Services.AddHealthChecks();
builder.Services.AddCatalogServices(builder.Configuration);

var app = builder.Build();

app.MapHealthCheckEndpoints();

await app.RunAsync();
