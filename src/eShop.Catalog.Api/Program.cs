using eShop.Catalog.Api.Data;
using eShop.Catalog.Api.Health;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddCatalogDbContext(builder.Configuration);

var app = builder.Build();

app.MapHealthCheckEndpoints();

await app.RunAsync();
