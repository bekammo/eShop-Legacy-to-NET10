using System.Net;
using eShop.Catalog.Api.Catalog;
using eShop.Catalog.Api.Data;
using eShop.Catalog.Api.IntegrationTests.Logging;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace eShop.Catalog.Api.IntegrationTests.Pictures;

// GET /items/{catalogItemId:int}/pic beyond the golden exchanges, which LegacyContractTests replays (ADR-0023): the
// legacy defects that the evidence shows, on items with the picture names that the legacy app let clients store. The
// host serves a pictures folder of the test's own, and secret.txt sits beside that folder, outside it.
[Trait("Category", "Docker")]
public sealed class PictureEndpointsTests(CatalogApiFactory factory) : IClassFixture<CatalogApiFactory>, IDisposable
{
    private static readonly byte[] Picture = [0x89, 0x50, 0x4E, 0x47];

    private readonly DirectoryInfo _directory = CreateDirectory();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private string PicturesFolder => Path.Combine(_directory.FullName, "pictures");

    public void Dispose() => _directory.Delete(recursive: true);

    // Audit D1 (pic-path-traversal-relative, pic-path-traversal-absolute): the legacy app served such files. null stands
    // for the absolute path of secret.txt, which only the test knows.
    [Theory]
    [InlineData("../secret.txt")]
    [InlineData(@"..\secret.txt")]
    [InlineData(null)]
    public async Task Picture_name_that_leaves_the_folder_is_a_404(string? name)
    {
        var id = await AddItemAsync(name ?? Path.Combine(_directory.FullName, "secret.txt"));
        await using var host = Host();
        using var client = host.CreateClient();

        using var response = await client.GetAsync($"/items/{id}/pic", CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("secret", await response.Content.ReadAsStringAsync(CancellationToken), StringComparison.Ordinal);
    }

    // Audit D7 (pic-missing-file): the legacy app answered 500. The warning tells an operator which item it was.
    [Fact]
    public async Task Missing_picture_file_is_a_404_and_a_warning()
    {
        var id = await AddItemAsync("missing.png");
        var logFile = Path.Combine(Path.GetDirectoryName(factory.LogFilePath)!, $"{Guid.NewGuid():N}.log");
        await using var host = Host(logFile);
        using var client = host.CreateClient();

        using var response = await client.GetAsync($"/items/{id}/pic", CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var warning = await LogFile.WaitForEventAsync(logFile, static logEvent => LogFile.EventName(logEvent) == "PictureNotFound");
        Assert.Equal("Warning", LogFile.String(warning, "@l"));
        Assert.Equal(id, warning.GetProperty("CatalogItemId").GetInt32());
        Assert.Equal("missing.png", LogFile.String(warning, "PictureFileName"));
    }

    // The file result sends the file's time, and answers a request that has the picture already with 304 (BC-010).
    [Fact]
    public async Task Picture_has_its_last_modified_time_and_a_conditional_request_gets_304()
    {
        var id = await AddItemAsync("UPPER.PNG");
        await using var host = Host();
        using var client = host.CreateClient();
        using var first = await client.GetAsync($"/items/{id}/pic", CancellationToken);
        using var conditional = new HttpRequestMessage(HttpMethod.Get, $"/items/{id}/pic");
        conditional.Headers.IfModifiedSince = first.Content.Headers.LastModified;

        using var response = await client.SendAsync(conditional, CancellationToken);

        Assert.NotNull(first.Content.Headers.LastModified);
        Assert.Equal(["bytes"], first.Headers.AcceptRanges);
        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(CancellationToken));
    }

    // The item endpoints of Stage 7.5 build PictureUri from the legacy route name.
    [Fact]
    public void Legacy_route_name_builds_the_picture_path()
    {
        var links = factory.Services.GetRequiredService<LinkGenerator>();

        Assert.Equal("/items/7/pic", links.GetPathByName("GetPicRouteTemplate", new { catalogItemId = 7 }));
    }

    // Without its folder, every picture would be a 404 (ADR-0023).
    [Theory]
    [InlineData("", "The PicturesPath field is required.")]
    [InlineData("no-such-folder", "Catalog:PicturesPath must name a folder that exists.")]
    public void Host_does_not_start_without_its_pictures_folder(string picturesPath, string message)
    {
        using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("Catalog:PicturesPath", picturesPath));

        var exception = Assert.ThrowsAny<Exception>(() => host.Services);

        Assert.Contains(message, exception.Message, StringComparison.Ordinal);
    }

    // A host that serves the test's pictures folder, and writes its log to the file given, if one is.
    private WebApplicationFactory<Program> Host(string? logFile = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Catalog:PicturesPath", PicturesFolder);
            if (logFile is not null)
            {
                CatalogApiFactory.UseLogFile(builder, logFile);
            }
        });

    // An item with the picture name given, written to the class's database as the legacy app let a client write it.
    private async Task<int> AddItemAsync(string pictureFileName)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var item = new CatalogItem { Name = "Picture test", PictureFileName = pictureFileName, Price = 1, CatalogTypeId = 1, CatalogBrandId = 1 };
        context.CatalogItems.Add(item);
        await context.SaveChangesAsync(CancellationToken);
        return item.Id;
    }

    private static DirectoryInfo CreateDirectory()
    {
        var directory = Directory.CreateTempSubdirectory("eShop.Catalog.Api.IntegrationTests-");
        var pictures = directory.CreateSubdirectory("pictures");
        File.WriteAllBytes(Path.Combine(pictures.FullName, "UPPER.PNG"), Picture);
        File.WriteAllText(Path.Combine(directory.FullName, "secret.txt"), "secret");
        return directory;
    }
}
