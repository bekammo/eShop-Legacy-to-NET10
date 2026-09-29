using eShop.Catalog.Api.Data;
using eShop.Catalog.Api.Health;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCatalogDbContext(builder.Configuration);
builder.Services.AddMigrateOnStartup();
builder.Services.AddHealthChecks()
    .AddCheck<CatalogDatabaseHealthCheck>(CatalogDatabaseHealthCheck.Name, tags: [HealthCheckEndpoints.ReadinessTag]);

var app = builder.Build();

app.MapHealthCheckEndpoints();

await app.RunAsync();
