using eShop.Catalog.Api.Catalog;
using eShop.Catalog.Api.Pictures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace eShop.Catalog.Api.UnitTests.Pictures;

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

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData(@"..\secret.txt")]
    [InlineData("1.png/../../secret.txt")]
    public void Name_that_leaves_the_folder_finds_nothing(string name)
    {
        using var pictures = Pictures("pictures");

        Assert.Null(pictures.Find(Item(name)));
    }

    [Fact]
    public void Rooted_name_finds_nothing()
    {
        using var pictures = Pictures("pictures");

        Assert.Null(pictures.Find(Item(Path.Combine(_contentRoot.FullName, "secret.txt"))));
    }

    [Theory]
    [InlineData("missing.png")]
    [InlineData("folder.png")]
    [InlineData("")]
    public void Name_without_a_file_finds_nothing(string name)
    {
        using var pictures = Pictures("pictures");

        Assert.Null(pictures.Find(Item(name)));
    }

    [Fact]
    public void Extension_case_does_not_change_the_content_type()
    {
        using var pictures = Pictures("pictures");

        Assert.Equal("image/png", pictures.Find(Item("UPPER.PNG"))?.ContentType);
    }

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
