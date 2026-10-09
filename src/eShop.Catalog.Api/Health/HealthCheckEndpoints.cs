using eShop.Catalog.Api.Logging;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog.Events;

namespace eShop.Catalog.Api.Health;

internal static class HealthCheckEndpoints
{
    internal const string LivenessPath = "/health/live";

    internal const string ReadinessPath = "/health/ready";

    internal const string ReadinessTag = "ready";

    // Runs no registered check on purpose: a failing database must take the instance out through readiness,
    // not get a healthy process restarted by liveness.
    internal static readonly HealthCheckOptions LivenessOptions = new() { Predicate = static _ => false };

    internal static readonly HealthCheckOptions ReadinessOptions = new() { Predicate = static check => check.Tags.Contains(ReadinessTag) };

    internal static readonly RequestLogLevel ProbeRequestLogLevel = new(LogEventLevel.Debug);

    internal static IEndpointRouteBuilder MapHealthCheckEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks(LivenessPath, LivenessOptions).WithMetadata(ProbeRequestLogLevel);
        endpoints.MapHealthChecks(ReadinessPath, ReadinessOptions).WithMetadata(ProbeRequestLogLevel);
        return endpoints;
    }
}
