using eShop.Catalog.Api.Health;
using eShop.Catalog.Api.Logging;
using Microsoft.AspNetCore.Http;
using Serilog.Events;

namespace eShop.Catalog.Api.UnitTests.Logging;

public sealed class RequestLoggingApplicationBuilderExtensionsTests
{
    [Theory]
    [InlineData(200, LogEventLevel.Information)]
    [InlineData(400, LogEventLevel.Information)]
    [InlineData(404, LogEventLevel.Information)]
    [InlineData(500, LogEventLevel.Error)]
    [InlineData(503, LogEventLevel.Error)]
    public void Status_sets_the_level_when_the_endpoint_sets_none(int status, LogEventLevel expected)
    {
        Assert.Equal(expected, Level(Request(status, endpointLevel: null), exception: null));
    }

    [Theory]
    [InlineData(200)]
    [InlineData(503)]
    public void Endpoint_level_applies_whatever_the_status(int status)
    {
        Assert.Equal(LogEventLevel.Debug, Level(Request(status, LogEventLevel.Debug), exception: null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(LogEventLevel.Debug)]
    public void Request_that_threw_is_an_Error_whatever_the_endpoint_level(LogEventLevel? endpointLevel)
    {
        Assert.Equal(LogEventLevel.Error, Level(Request(200, endpointLevel), new InvalidOperationException()));
    }

    [Theory]
    [InlineData(null, LogEventLevel.Information)]
    [InlineData(LogEventLevel.Debug, LogEventLevel.Debug)]
    public void Request_cancelled_because_the_client_went_away_is_not_an_Error(LogEventLevel? endpointLevel, LogEventLevel expected)
    {
        var request = Request(200, endpointLevel);
        request.RequestAborted = new CancellationToken(canceled: true);

        Assert.Equal(expected, Level(request, new OperationCanceledException(request.RequestAborted)));
    }

    [Fact]
    public void Request_cancelled_while_the_client_waits_is_an_Error()
    {
        Assert.Equal(LogEventLevel.Error, Level(Request(200, endpointLevel: null), new OperationCanceledException()));
    }

    [Fact]
    public void Health_checks_ask_for_Debug()
    {
        Assert.Equal(LogEventLevel.Debug, HealthCheckEndpoints.ProbeRequestLogLevel.Level);
    }

    private static LogEventLevel Level(HttpContext request, Exception? exception) =>
        RequestLoggingApplicationBuilderExtensions.RequestLevel(request, elapsedMilliseconds: 1, exception);

    private static DefaultHttpContext Request(int status, LogEventLevel? endpointLevel)
    {
        var request = new DefaultHttpContext { Response = { StatusCode = status } };
        var metadata = endpointLevel is { } level ? new EndpointMetadataCollection(new RequestLogLevel(level)) : EndpointMetadataCollection.Empty;
        request.SetEndpoint(new Endpoint(requestDelegate: null, metadata, "test endpoint"));
        return request;
    }
}
