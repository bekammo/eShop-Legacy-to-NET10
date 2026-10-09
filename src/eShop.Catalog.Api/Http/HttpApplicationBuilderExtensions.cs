namespace eShop.Catalog.Api.Http;

internal static class HttpApplicationBuilderExtensions
{
    // UseRouting is called last on purpose: without it WebApplication runs routing before all the app's middleware,
    // and an exception that routing throws, such as an ambiguous match, would escape this error handling.
    internal static IApplicationBuilder UseCatalogErrorHandling(this IApplicationBuilder app) =>
        app.UseExceptionHandler().UseStatusCodePages().UseRouting();
}
