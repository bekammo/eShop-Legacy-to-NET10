using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace eShop.Catalog.Api.UnitTests;

// A host environment with a content root of the test's choice, for the code that resolves paths against it.
internal sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Testing";

    public string ApplicationName { get; set; } = "eShop.Catalog.Api";

    public string ContentRootPath { get; set; } = contentRootPath;

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
