namespace eShop.Catalog.Api.Http;

internal static class HttpApplicationBuilderExtensions
{
    // Every error as a problem (ADR-0021): the exception handler turns an exception into a 500, and the status code
    // pages give a problem body to an error status that has none, such as a route that matches nothing, a method that
    // the route does not allow, a parameter that does not bind, or an endpoint's TypedResults.NotFound(). Program.cs
    // adds this after the request logging, so the request event has the status that the client gets, and the
    // exception is logged once, by the handler.
    // Routing comes after them, so that they also handle what routing throws, such as the exception for a request that
    // two endpoints match. WebApplication would otherwise run routing first, before any of the app's middleware.
    internal static IApplicationBuilder UseCatalogErrorHandling(this IApplicationBuilder app) =>
        app.UseExceptionHandler().UseStatusCodePages().UseRouting();
}
