using eShop.Catalog.Api.Health;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace eShop.Catalog.Api.UnitTests.Health;

public sealed class ReadinessPolicyTests
{
    [Fact]
    public async Task Readiness_runs_only_the_checks_tagged_ready()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHealthChecks()
            .AddCheck("ready dependency", static () => HealthCheckResult.Healthy(), tags: [HealthCheckEndpoints.ReadinessTag])
            .AddCheck("untagged failing check", static () => HealthCheckResult.Unhealthy());
        await using var provider = services.BuildServiceProvider();
        var healthChecks = provider.GetRequiredService<HealthCheckService>();

        var report = await healthChecks.CheckHealthAsync(
            HealthCheckEndpoints.ReadinessOptions.Predicate,
            TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Healthy, report.Status);
        Assert.Equal(["ready dependency"], report.Entries.Keys);
    }
}
