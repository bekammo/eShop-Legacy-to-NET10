using eShop.Catalog.Api.Catalog;
using eShop.Catalog.Api.Pictures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace eShop.Catalog.Api.UnitTests.Pictures;

// The lookup of an item's picture (ADR-0023), in a temporary content root: the pictures are in its pictures folder, and
// secret.txt is beside that folder, outside it.
public sealed class CatalogPicturesTests : IDisposable
{
    private readonly DirectoryInfo _contentRoot = Directory.CreateTempSubdirectory("eShop.Catalog.Api.UnitTests-");

    public CatalogPicturesTests()
    {
        var pictures = _contentRoot.CreateSubdirectory("pictures");
        File.WriteAllBytes(Path.Combine(pictures.FullName, "1.png"), [1]);
        File.WriteAllBytes(Path.Combine(pictures.FullName, "UPPER.PNG"), [1]);
        File.WriteAllBytes(Path.Combine(pictures.FullName, "notes.unknown"), [1]);
        pictures.CreateSubdirectory("folder.png");
        File.WriteAllText(Path.Combine(_contentRoot.FullName, "secret.txt"), "secret");
    }

    public void Dispose() => _contentRoot.Delete(recursive: true);

    // The committed setting is relative too, to the content root (ADR-0018 resolves the log file the same way).
    [Fact]
    public void Relative_folder_is_resolved_against_the_content_root()
    {
        using var pictures = Pictures("pictures");

        var picture = pictures.Find(Item("1.png"));

        Assert.NotNull(picture);
        Assert.Equal(Path.Combine(_contentRoot.FullName, "pictures", "1.png"), picture.PhysicalPath);
        Assert.Equal("image/png", picture.ContentType);
    }

    [Fact]
    public void Absolute_folder_is_used_as_it_is()
    {
        using var pictures = Pictures(Path.Combine(_contentRoot.FullName, "pictures"));

        Assert.NotNull(pictures.Find(Item("1.png")));
    }

    // Audit D1: the legacy app served the file that the name pointed at, inside the folder or not. On Linux a
    // backslash is part of a file name, so ..\secret.txt names a file in the folder, which does not exist either.
    [Theory]
    [InlineData("../secret.txt")]
    [InlineData(@"..\secret.txt")]
    [InlineData("1.png/../../secret.txt")]
    public void Name_that_leaves_the_folder_finds_nothing(string name)
    {
        using var pictures = Pictures("pictures");

        Assert.Null(pictures.Find(Item(name)));
    }

    // C:\Windows\win.ini in the evidence. A rooted name is refused on Windows. On Linux the provider trims the leading
    // slash and looks the rest up inside the folder, where it is not.
    [Fact]
    public void Rooted_name_finds_nothing()
    {
        using var pictures = Pictures("pictures");

        Assert.Null(pictures.Find(Item(Path.Combine(_contentRoot.FullName, "secret.txt"))));
    }

    // Audit D7: the legacy app answered a missing file with a 500.
    [Theory]
    [InlineData("missing.png")]
    [InlineData("folder.png")]
    [InlineData("")]
    public void Name_without_a_file_finds_nothing(string name)
    {
        using var pictures = Pictures("pictures");

        Assert.Null(pictures.Find(Item(name)));
    }

    // Audit D8: the legacy MIME switch was case-sensitive, and sent 1.PNG as application/octet-stream.
    [Fact]
    public void Extension_case_does_not_change_the_content_type()
    {
        using var pictures = Pictures("pictures");

        Assert.Equal("image/png", pictures.Find(Item("UPPER.PNG"))?.ContentType);
    }

    // As the legacy app sent an extension that it did not know.
    [Fact]
    public void Unknown_extension_is_sent_as_octet_stream()
    {
        using var pictures = Pictures("pictures");

        Assert.Equal("application/octet-stream", pictures.Find(Item("notes.unknown"))?.ContentType);
    }

    private CatalogPictures Pictures(string picturesPath) =>
        new(Options.Create(new CatalogOptions { PicturesPath = picturesPath }), new TestHostEnvironment(_contentRoot.FullName), NullLogger<CatalogPictures>.Instance);

    private static CatalogItem Item(string pictureFileName) => new() { Id = 1, Name = "Item", PictureFileName = pictureFileName };
}
