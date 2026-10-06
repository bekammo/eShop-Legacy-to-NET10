using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace eShop.Catalog.Api.IntegrationTests.Health;

public sealed class LivenessEndpointTests(MockModeCatalogApiFactory factory) : IClassFixture<MockModeCatalogApiFactory>
{
    [Fact]
    public async Task Get_returns_200_Healthy_as_plain_text()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Get_stays_healthy_when_a_registered_check_fails()
    {
        using var failingDependency = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddHealthChecks()
                    .AddCheck("failing dependency", static () => HealthCheckResult.Unhealthy())));
        using var client = failingDependency.CreateClient();

        using var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
