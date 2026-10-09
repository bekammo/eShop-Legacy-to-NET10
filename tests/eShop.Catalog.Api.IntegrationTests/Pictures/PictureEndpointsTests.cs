using System.Net;
using eShop.Catalog.Api.Catalog;
using eShop.Catalog.Api.Data;
using eShop.Catalog.Api.IntegrationTests.Logging;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace eShop.Catalog.Api.IntegrationTests.Pictures;

[Trait("Category", "Docker")]
public sealed class PictureEndpointsTests(CatalogApiFactory factory) : IClassFixture<CatalogApiFactory>, IDisposable
{
    private static readonly byte[] Picture = [0x89, 0x50, 0x4E, 0x47];

    private readonly DirectoryInfo _directory = CreateDirectory();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private string PicturesFolder => Path.Combine(_directory.FullName, "pictures");

    public void Dispose() => _directory.Delete(recursive: true);

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

    [Fact]
    public void Legacy_route_name_builds_the_picture_path()
    {
        var links = factory.Services.GetRequiredService<LinkGenerator>();

        Assert.Equal("/items/7/pic", links.GetPathByName("GetPicRouteTemplate", new { catalogItemId = 7 }));
    }

    [Theory]
    [InlineData("", "The PicturesPath field is required.")]
    [InlineData("no-such-folder", "Catalog:PicturesPath must name a folder that exists.")]
    public void Host_does_not_start_without_its_pictures_folder(string picturesPath, string message)
    {
        using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("Catalog:PicturesPath", picturesPath));

        var exception = Assert.ThrowsAny<Exception>(() => host.Services);

        Assert.Contains(message, exception.Message, StringComparison.Ordinal);
    }

    private WebApplicationFactory<Program> Host(string? logFile = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Catalog:PicturesPath", PicturesFolder);
            if (logFile is not null)
            {
                CatalogApiFactory.UseLogFile(builder, logFile);
            }
        });

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
