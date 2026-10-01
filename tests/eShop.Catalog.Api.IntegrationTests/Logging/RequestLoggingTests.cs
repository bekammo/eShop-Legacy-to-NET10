using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace eShop.Catalog.Api.IntegrationTests.Logging;

// One event per request, in place of the legacy Application_BeginRequest event and its requestinfo and activityid
// properties (ADR-0019). Each request carries a W3C traceparent header, as from a calling service, so its trace ID
// is known and finds its events.
public sealed class RequestLoggingTests(CatalogApiFactory factory) : IClassFixture<CatalogApiFactory>
{
    private const string RequestLoggingMiddleware = "Serilog.AspNetCore.RequestLoggingMiddleware";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // The route matches nothing, which no later endpoint can change: a 404 is an Information request event too.
    [Fact]
    public async Task Request_is_logged_once_with_its_trace_id_query_string_and_user_agent()
    {
        using var client = factory.CreateClient();
        var traceId = ActivityTraceId.CreateRandom().ToHexString();

        using var response = await client.SendAsync(Request("/no-such-route?pageSize=10", traceId), CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var logEvent = await RequestEventAsync(factory.LogFilePath, traceId);
        Assert.Equal("HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms", LogFile.String(logEvent, "@mt"));
        Assert.Equal("GET", LogFile.String(logEvent, "RequestMethod"));
        Assert.Equal("/no-such-route", LogFile.String(logEvent, "RequestPath"));
        Assert.Equal(404, logEvent.GetProperty("StatusCode").GetInt32());
        Assert.Equal(JsonValueKind.Number, logEvent.GetProperty("Elapsed").ValueKind);
        Assert.Equal("?pageSize=10", LogFile.String(logEvent, "QueryString"));
        Assert.Equal("RequestLoggingTests/1.0", LogFile.String(logEvent, "UserAgent"));
        // Information, which CLEF leaves out.
        Assert.Null(LogFile.String(logEvent, "@l"));
        // ASP.NET Core logs "Request starting" before the request runs, so it would be in the file by now.
        Assert.Equal([RequestLoggingMiddleware], LogFile.Events(factory.LogFilePath)
            .Where(other => LogFile.String(other, "@tr") == traceId)
            .Select(static other => LogFile.String(other, "SourceContext")));
    }

    [Fact]
    public async Task Request_that_throws_is_logged_at_Error_with_the_exception()
    {
        var logFile = LogFilePath();
        await using var host = factory.WithWebHostBuilder(builder =>
            CatalogApiFactory.UseLogFile(builder, logFile)
                .ConfigureTestServices(static services => services.AddTransient<IStartupFilter, ThrowAfterEndpoints>()));
        using var client = host.CreateClient();
        var traceId = ActivityTraceId.CreateRandom().ToHexString();

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(Request("/throw", traceId), CancellationToken));

        var logEvent = await RequestEventAsync(logFile, traceId);
        Assert.Equal("Error", LogFile.String(logEvent, "@l"));
        Assert.Equal(500, logEvent.GetProperty("StatusCode").GetInt32());
        Assert.StartsWith($"System.InvalidOperationException: {ThrowAfterEndpoints.Message}", LogFile.String(logEvent, "@x"), StringComparison.Ordinal);
    }

    // Probes call the health checks every few seconds. The host logs Debug events here, so that the events are written.
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_check_requests_are_logged_at_Debug(string path)
    {
        var logFile = LogFilePath();
        await using var host = factory.WithWebHostBuilder(builder =>
            CatalogApiFactory.UseLogFile(builder, logFile)
                .ConfigureAppConfiguration(static (_, configuration) =>
                    configuration.AddInMemoryCollection([new("Serilog:MinimumLevel:Default", "Debug")])));
        using var client = host.CreateClient();
        var traceId = ActivityTraceId.CreateRandom().ToHexString();

        using var response = await client.SendAsync(Request(path, traceId), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var logEvent = await RequestEventAsync(logFile, traceId);
        Assert.Equal("Debug", LogFile.String(logEvent, "@l"));
        Assert.Equal(path, LogFile.String(logEvent, "RequestPath"));
    }

    private static HttpRequestMessage Request(string path, string traceId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom().ToHexString()}-01");
        request.Headers.UserAgent.ParseAdd("RequestLoggingTests/1.0");
        return request;
    }

    private static Task<JsonElement> RequestEventAsync(string logFile, string traceId) =>
        LogFile.WaitForEventAsync(logFile, logEvent =>
            LogFile.String(logEvent, "@tr") == traceId && LogFile.String(logEvent, "SourceContext") == RequestLoggingMiddleware);

    // A file beside the factory's own, which the factory deletes with its directory.
    private string LogFilePath() =>
        Path.Combine(Path.GetDirectoryName(factory.LogFilePath)!, $"{Guid.NewGuid():N}.log");

    // Throws for every request that no endpoint takes, inside the request logging: the filter runs the app's own
    // pipeline first, and adds this middleware after it.
    private sealed class ThrowAfterEndpoints : IStartupFilter
    {
        public const string Message = "Thrown by the request logging test.";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                next(app);
                app.Run(static _ => throw new InvalidOperationException(Message));
            };
    }
}
