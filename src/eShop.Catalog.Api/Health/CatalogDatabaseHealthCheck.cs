using eShop.Catalog.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace eShop.Catalog.Api.Health;

// Readiness of the catalog database (ADR-0013): the API can connect to it, and it has every migration that
// this build knows, so that no request meets an older schema. The endpoint answers with the status only;
// the description reaches the logs.
internal sealed class CatalogDatabaseHealthCheck(CatalogDbContext context) : IHealthCheck
{
    internal const string Name = "catalog-database";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext healthCheckContext, CancellationToken cancellationToken = default)
    {
        if (!await context.Database.CanConnectAsync(cancellationToken))
        {
            return new HealthCheckResult(healthCheckContext.Registration.FailureStatus, "Cannot connect to the catalog database.");
        }

        var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        return pending.Count == 0
            ? HealthCheckResult.Healthy()
            : new HealthCheckResult(
                healthCheckContext.Registration.FailureStatus,
                "The catalog database lacks migrations: " + string.Join(", ", pending) + ".");
    }
}
