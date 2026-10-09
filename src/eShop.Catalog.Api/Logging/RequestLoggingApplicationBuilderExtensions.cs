using Serilog;
using Serilog.Events;

namespace eShop.Catalog.Api.Logging;

internal static class RequestLoggingApplicationBuilderExtensions
{
    internal static IApplicationBuilder UseCatalogRequestLogging(this IApplicationBuilder app) =>
        app.UseSerilogRequestLogging(options =>
        {
            // Not redundant: without it the middleware writes to the static Log.Logger, which stays silent in this
            // host.
            options.Logger = app.ApplicationServices.GetRequiredService<Serilog.ILogger>();
            options.EnrichDiagnosticContext = static (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("QueryString", httpContext.Request.QueryString.Value ?? string.Empty);
                diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.ToString());
            };
            options.GetLevel = RequestLevel;
        });

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
