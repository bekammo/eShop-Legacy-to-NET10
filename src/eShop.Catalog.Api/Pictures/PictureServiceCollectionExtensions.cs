using eShop.Catalog.Api.Catalog;

namespace eShop.Catalog.Api.Pictures;

internal static class PictureServiceCollectionExtensions
{
    // The pictures folder of Catalog:PicturesPath, one for the app (ADR-0023). Its folder is checked when the host starts,
    // with the rest of the Catalog section (ValidateOnStart, ADR-0009): without it every picture would be a 404.
    internal static IServiceCollection AddCatalogPictures(this IServiceCollection services)
    {
        services.AddOptions<CatalogOptions>()
            .Validate<IHostEnvironment>(
                static (options, environment) =>
                    string.IsNullOrWhiteSpace(options.PicturesPath)
                    || Directory.Exists(CatalogPictures.Root(options.PicturesPath, environment.ContentRootPath)),
                "Catalog:PicturesPath must name a folder that exists. A relative path is resolved against the content root.");
        return services.AddSingleton<CatalogPictures>();
    }
}
