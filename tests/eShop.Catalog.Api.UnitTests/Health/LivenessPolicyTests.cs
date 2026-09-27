using eShop.Catalog.Api.Health;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace eShop.Catalog.Api.UnitTests.Health;

public sealed class LivenessPolicyTests
{
    [Fact]
    public async Task Liveness_runs_no_registered_check()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHealthChecks()
            .AddCheck("failing dependency", static () => HealthCheckResult.Unhealthy());
        await using var provider = services.BuildServiceProvider();
        var healthChecks = provider.GetRequiredService<HealthCheckService>();

        var report = await healthChecks.CheckHealthAsync(
            HealthCheckEndpoints.LivenessOptions.Predicate,
            TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Healthy, report.Status);
        Assert.Empty(report.Entries);
    }
}
