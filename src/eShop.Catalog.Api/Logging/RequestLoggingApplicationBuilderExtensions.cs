using Serilog;
using Serilog.Events;

namespace eShop.Catalog.Api.Logging;

internal static class RequestLoggingApplicationBuilderExtensions
{
    // One event for each request, written when the request completes, in place of the legacy Application_BeginRequest
    // event and its log4net properties (ADR-0019). The message has the method, the path, the status and the time.
    // The query string and the user agent, which log4net's requestinfo held, are properties, and the trace ID of the
    // request's activity takes the place of activityid.
    internal static IApplicationBuilder UseCatalogRequestLogging(this IApplicationBuilder app) =>
        app.UseSerilogRequestLogging(options =>
        {
            // The host's logger. Without one the middleware writes to the static Log.Logger, which stays silent
            // (ADR-0018).
            options.Logger = app.ApplicationServices.GetRequiredService<Serilog.ILogger>();
            options.EnrichDiagnosticContext = static (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("QueryString", httpContext.Request.QueryString.Value ?? string.Empty);
                diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.ToString());
            };
            options.GetLevel = RequestLevel;
        });

    // Error for a request that threw, whatever its endpoint, unless it was cancelled because the client went away.
    // Otherwise the level that the endpoint asks for, such as Debug for the health checks, which probes call every
    // few seconds. Otherwise Error for a server error, and Information for the rest.
    internal static LogEventLevel RequestLevel(HttpContext httpContext, double elapsedMilliseconds, Exception? exception)
    {
        if (exception is not null && !(exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested))
        {
            return LogEventLevel.Error;
        }

        if (httpContext.GetEndpoint()?.Metadata.GetMetadata<RequestLogLevel>() is { } endpointLevel)
        {
            return endpointLevel.Level;
        }

        return httpContext.Response.StatusCode >= StatusCodes.Status500InternalServerError ? LogEventLevel.Error : LogEventLevel.Information;
    }
}
