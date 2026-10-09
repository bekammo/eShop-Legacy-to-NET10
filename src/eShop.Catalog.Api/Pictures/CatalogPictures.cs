using eShop.Catalog.Api.Catalog;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace eShop.Catalog.Api.Pictures;

// Look pictures up only through the PhysicalFileProvider, which finds no file outside the folder. The database
// may hold names such as ..\Global.asax or C:\Windows\win.ini, which Path.Combine would serve.
internal sealed partial class CatalogPictures(IOptions<CatalogOptions> options, IHostEnvironment environment, ILogger<CatalogPictures> logger)
    : IDisposable
{
    private const string UnknownContentType = "application/octet-stream";

    private readonly PhysicalFileProvider _files = new(Root(options.Value.PicturesPath!, environment.ContentRootPath));

    private readonly FileExtensionContentTypeProvider _contentTypes = new();

    internal static string Root(string picturesPath, string contentRoot) => Path.GetFullPath(picturesPath, contentRoot);

    public CatalogPicture? Find(CatalogItem item)
    {
        var file = _files.GetFileInfo(item.PictureFileName);
        if (!file.Exists || file.PhysicalPath is null)
        {
            PictureNotFound(logger, item.Id, item.PictureFileName);
            return null;
        }

        var contentType = _contentTypes.TryGetContentType(item.PictureFileName, out var known) ? known : UnknownContentType;
        return new CatalogPicture(file.PhysicalPath, contentType);
    }

    public void Dispose() => _files.Dispose();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The picture {PictureFileName} of item {CatalogItemId} is not a file in the pictures folder")]
    private static partial void PictureNotFound(ILogger logger, int catalogItemId, string pictureFileName);
}

internal sealed record CatalogPicture(string PhysicalPath, string ContentType);
