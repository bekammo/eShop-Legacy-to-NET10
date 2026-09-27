using eShop.Catalog.Api.Health;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthCheckEndpoints();

await app.RunAsync();
