using System.Diagnostics;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace eShop.Catalog.Api.Http;

// Writes every problem as application/problem+json (RFC 9457), whatever the request's Accept header says, as the
// endpoints write every result as JSON (ADR-0021). ASP.NET Core's own writer writes only for a request that accepts
// JSON. For any other Accept header, such as application/xml or text/html, the status code pages wrote
// "Status Code: 404; Not Found" as text/plain, the exception handler a 500 without a body, and TypedResults.Problem a
// problem without its trace ID.
internal sealed class ProblemJsonWriter(IOptions<JsonOptions> jsonOptions, IOptions<ProblemDetailsOptions> problemDetailsOptions) : IProblemDetailsWriter
{
    private const string ContentType = "application/problem+json";

    public bool CanWrite(ProblemDetailsContext context) => true;

    public ValueTask WriteAsync(ProblemDetailsContext context)
    {
        var httpContext = context.HttpContext;
        var problem = context.ProblemDetails;
        problem.Status ??= httpContext.Response.StatusCode;

        // The type and title that ASP.NET Core gives the status, as TypedResults.Problem applies them.
        var defaults = TypedResults.Problem(statusCode: problem.Status).ProblemDetails;
        problem.Type ??= defaults.Type;
        problem.Title ??= defaults.Title;

        // The W3C ID of the request's activity, 00-{trace ID}-{span ID}-{flags}, as ASP.NET Core's writer sets it. Its
        // trace ID is the @tr of the request's log events (ADR-0018). Set, not added, so that a problem written before,
        // for another request, does not keep that request's ID.
        problem.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        problemDetailsOptions.Value.CustomizeProblemDetails?.Invoke(context);

        // Without a token, so that a client that has gone away does not make the writing throw: the exception handler
        // would then log the exception it handles a second time, as its own handler's failure.
        var serializerOptions = jsonOptions.Value.SerializerOptions;
        return new ValueTask(httpContext.Response.WriteAsJsonAsync(problem, serializerOptions.GetTypeInfo(problem.GetType()), ContentType));
    }
}
