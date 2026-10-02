using eShop.Catalog.Api.Catalog;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace eShop.Catalog.Api.Pictures;

// The item pictures: the files of the folder that Catalog:PicturesPath names (ADR-0023). The folder is resolved once,
// for the first picture that a client asks for; the host checks at startup that it exists. A picture is looked up by the item's PictureFileName through a PhysicalFileProvider, which finds
// only files inside the folder: a name that leaves it, such as ..\Global.asax, or a rooted one, such as
// C:\Windows\win.ini, finds nothing. The legacy app combined the folder and the name, and served whatever file that
// pointed at (audit D1). A database adopted from it may still hold such names (ADR-0012).
internal sealed partial class CatalogPictures(IOptions<CatalogOptions> options, IHostEnvironment environment, ILogger<CatalogPictures> logger)
    : IDisposable
{
    // What the legacy app sent for an extension that it did not know.
    private const string UnknownContentType = "application/octet-stream";

    private readonly PhysicalFileProvider _files = new(Root(options.Value.PicturesPath!, environment.ContentRootPath));

    private readonly FileExtensionContentTypeProvider _contentTypes = new();

    // The folder as an absolute path. A relative one is taken from the content root.
    internal static string Root(string picturesPath, string contentRoot) => Path.GetFullPath(picturesPath, contentRoot);

    // The item's picture, or null when the folder has no file of that name: a missing file, which the legacy app answered
    // with a 500 (D7), a folder, or a name that leaves the folder. Its content type comes from the extension, whatever
    // the extension's case (D8).
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

    // A missing file or a misconfigured folder, or a name that tries to leave the folder: each needs an operator.
    [LoggerMessage(Level = LogLevel.Warning, Message = "The picture {PictureFileName} of item {CatalogItemId} is not a file in the pictures folder")]
    private static partial void PictureNotFound(ILogger logger, int catalogItemId, string pictureFileName);
}

// A picture file, and the content type to send it with.
internal sealed record CatalogPicture(string PhysicalPath, string ContentType);
