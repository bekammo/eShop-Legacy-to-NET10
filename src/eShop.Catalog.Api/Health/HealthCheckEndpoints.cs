using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace eShop.Catalog.Api.Health;

internal static class HealthCheckEndpoints
{
    internal const string LivenessPath = "/health/live";

    // Liveness answers "is the process up and serving requests?". It runs no registered check:
    // a failing dependency such as the database must take the instance out of rotation through
    // readiness (Stage 4.4), not get a healthy process restarted.
    internal static readonly HealthCheckOptions LivenessOptions = new() { Predicate = static _ => false };

    internal static IEndpointRouteBuilder MapHealthCheckEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks(LivenessPath, LivenessOptions);
        return endpoints;
    }
}
