using System.Diagnostics;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace eShop.Catalog.Api.Http;

internal sealed class ProblemJsonWriter(IOptions<JsonOptions> jsonOptions, IOptions<ProblemDetailsOptions> problemDetailsOptions) : IProblemDetailsWriter
{
    private const string ContentType = "application/problem+json";

    public bool CanWrite(ProblemDetailsContext context) => true;

    public ValueTask WriteAsync(ProblemDetailsContext context)
    {
        var httpContext = context.HttpContext;
        var problem = context.ProblemDetails;
        problem.Status ??= httpContext.Response.StatusCode;

        var defaults = TypedResults.Problem(statusCode: problem.Status).ProblemDetails;
        problem.Type ??= defaults.Type;
        problem.Title ??= defaults.Title;

        problem.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        problemDetailsOptions.Value.CustomizeProblemDetails?.Invoke(context);

        // No cancellation token on purpose: for a client that has gone, the write would throw and the exception handler
        // would log the exception a second time, as its own handler's failure.
        var serializerOptions = jsonOptions.Value.SerializerOptions;
        return new ValueTask(httpContext.Response.WriteAsJsonAsync(problem, serializerOptions.GetTypeInfo(problem.GetType()), ContentType));
    }
}
