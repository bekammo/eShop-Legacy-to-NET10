using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace eShop.Catalog.Api.IntegrationTests.Http;

public sealed class ErrorHandlingTests(MockModeCatalogApiFactory factory) : IClassFixture<MockModeCatalogApiFactory>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Route_that_matches_nothing_is_a_404_problem_with_the_trace_id_of_the_request()
    {
        using var client = factory.CreateClient();
        var traceId = ActivityTraceId.CreateRandom().ToHexString();

        using var response = await client.SendAsync(Request("/no-such-route", traceId), CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.ToString());
        var problem = await ProblemAsync(response);
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.5", problem.GetProperty("type").GetString());
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
        Assert.Matches($"^00-{traceId}-[0-9a-f]{{16}}-0[01]$", problem.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task Exception_is_a_500_problem_that_does_not_show_the_exception()
    {
        await using var host = factory.WithWebHostBuilder(static builder =>
            builder.ConfigureTestServices(static services => services.AddSingleton<IStartupFilter>(new ThrowAfterEndpoints(afterResponseStarted: false))));
        using var client = host.CreateClient();
        var traceId = ActivityTraceId.CreateRandom().ToHexString();

        using var response = await client.SendAsync(Request("/throw", traceId), CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.ToString());
        var problem = await ProblemAsync(response);
        Assert.Equal(["type", "title", "status", "traceId"], problem.EnumerateObject().Select(static member => member.Name));
        Assert.Matches($"^00-{traceId}-", problem.GetProperty("traceId").GetString());
    }

    // TestServer never sends a Server header, so a response check would always pass: the Kestrel option is checked.
    [Fact]
    public void Kestrel_sends_no_Server_header()
    {
        Assert.False(factory.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value.AddServerHeader);
    }

    private static HttpRequestMessage Request(string path, string traceId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom().ToHexString()}-01");
        return request;
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return document.RootElement.Clone();
    }
}
