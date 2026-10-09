using eShop.Catalog.Api.Catalog;

namespace eShop.Catalog.Api.Pictures;

internal static class PictureServiceCollectionExtensions
{
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
