using System.Data.Common;
using eShop.Catalog.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace eShop.Catalog.Api.Health;

internal sealed class CatalogDatabaseHealthCheck(CatalogDbContext context) : IHealthCheck
{
    internal const string Name = "catalog-database";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext healthCheckContext, CancellationToken cancellationToken = default)
    {
        try
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
        catch (DbException exception) when (cancellationToken.IsCancellationRequested)
        {
            // SqlClient reports a cancelled command as a SqlException. Rethrown as a cancellation, so that a probe
            // that went away is not logged at Error as a failed check.
            throw new OperationCanceledException("The request was aborted while the check ran a command.", exception, cancellationToken);
        }
    }
}
