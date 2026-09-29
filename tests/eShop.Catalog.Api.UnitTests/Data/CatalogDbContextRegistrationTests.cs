using eShop.Catalog.Api.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace eShop.Catalog.Api.UnitTests.Data;

public sealed class CatalogDbContextRegistrationTests
{
    // The production case: no source sets the key at all. The integration tests cover empty and blank values.
    [Fact]
    public void Registration_fails_when_the_connection_string_is_missing()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddCatalogDbContext(configuration));

        Assert.Contains("'ConnectionStrings:CatalogDb' is not set", exception.Message, StringComparison.Ordinal);
    }
}
