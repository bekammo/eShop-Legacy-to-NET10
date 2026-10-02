using eShop.Catalog.Api.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace eShop.Catalog.Api.UnitTests.Http;

// What AddCatalogHttp registers (ADR-0020, ADR-0021), without a host.
public sealed class HttpServiceCollectionExtensionsTests
{
    // IProblemDetailsService asks the writers in the order in which they are registered, and ProblemJsonWriter writes
    // for every request, so ASP.NET Core's writer, which AddProblemDetails registers, is never used.
    [Fact]
    public void Problem_json_writer_comes_before_ASP_NET_Core_s_writer()
    {
        var writers = new ServiceCollection().AddCatalogHttp()
            .Where(static service => service.ServiceType == typeof(IProblemDetailsWriter))
            .Select(static service => service.ImplementationType)
            .ToList();

        Assert.Equal(typeof(ProblemJsonWriter), writers[0]);
        Assert.Equal(2, writers.Count);
    }
}
