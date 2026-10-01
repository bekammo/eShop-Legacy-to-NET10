using eShop.Catalog.Api.Logging;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog.Events;

namespace eShop.Catalog.Api.Health;

internal static class HealthCheckEndpoints
{
    internal const string LivenessPath = "/health/live";

    internal const string ReadinessPath = "/health/ready";

    // Registered checks with this tag decide readiness.
    internal const string ReadinessTag = "ready";

    // Liveness answers "is the process up and serving requests?". It runs no registered check:
    // a failing dependency such as the database must take the instance out of rotation through
    // readiness, not get a healthy process restarted.
    internal static readonly HealthCheckOptions LivenessOptions = new() { Predicate = static _ => false };

    // Readiness answers "can this instance serve the API now?": 200 when every check tagged "ready"
    // passes, otherwise 503 (ADR-0013).
    internal static readonly HealthCheckOptions ReadinessOptions = new() { Predicate = static check => check.Tags.Contains(ReadinessTag) };

    // Probes call both every few seconds, so their request events are Debug, whatever the status (ADR-0019). A
    // failing check is still logged, at Error, by the health check service.
    internal static readonly RequestLogLevel ProbeRequestLogLevel = new(LogEventLevel.Debug);

    internal static IEndpointRouteBuilder MapHealthCheckEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks(LivenessPath, LivenessOptions).WithMetadata(ProbeRequestLogLevel);
        endpoints.MapHealthChecks(ReadinessPath, ReadinessOptions).WithMetadata(ProbeRequestLogLevel);
        return endpoints;
    }
}
