using Microsoft.AspNetCore.Http.HttpResults;

namespace eShop.Catalog.Api.Files;

internal static class FileEndpoints
{
    private const string Detail =
        "GET /api/files has been retired. It returned the brands as a BinaryFormatter payload, which is unsafe to " +
        "deserialize. GET /api/brands returns them as JSON.";

    internal static IEndpointRouteBuilder MapFileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var files = endpoints.MapGroup("/api/files").ExcludeFromDescription();
        files.MapGet("", Retired);
        files.MapGet("/{id}", Retired);
        return endpoints;
    }

    private static ProblemHttpResult Retired() => TypedResults.Problem(statusCode: StatusCodes.Status410Gone, detail: Detail);
}
