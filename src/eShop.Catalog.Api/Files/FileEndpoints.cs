using Microsoft.AspNetCore.Http.HttpResults;

namespace eShop.Catalog.Api.Files;

// The legacy GET /api/files, retired (ADR-0022). It returned the brands as a BinaryFormatter payload, which is unsafe to
// deserialize, so it answers 410 Gone with a problem that points to GET /api/brands, which returns the same brands as
// JSON. It is not in the OpenAPI document, so that no new client starts to call it.
internal static class FileEndpoints
{
    private const string Detail =
        "GET /api/files has been retired. It returned the brands as a BinaryFormatter payload, which is unsafe to " +
        "deserialize. GET /api/brands returns them as JSON.";

    internal static IEndpointRouteBuilder MapFileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // The legacy route, api/{controller}/{id}, gave the same answer whatever the ID.
        var files = endpoints.MapGroup("/api/files").ExcludeFromDescription();
        files.MapGet("", Retired);
        files.MapGet("/{id}", Retired);
        return endpoints;
    }

    private static ProblemHttpResult Retired() => TypedResults.Problem(statusCode: StatusCodes.Status410Gone, detail: Detail);
}
