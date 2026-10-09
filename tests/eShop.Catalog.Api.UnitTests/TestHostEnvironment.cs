using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace eShop.Catalog.Api.UnitTests;

internal sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Testing";

    public string ApplicationName { get; set; } = "eShop.Catalog.Api";

    public string ContentRootPath { get; set; } = contentRootPath;

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
