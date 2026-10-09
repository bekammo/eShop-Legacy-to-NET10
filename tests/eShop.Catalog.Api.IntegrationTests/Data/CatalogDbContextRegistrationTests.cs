using eShop.Catalog.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace eShop.Catalog.Api.IntegrationTests.Data;

[Trait("Category", "Docker")]
public sealed class CatalogDbContextRegistrationTests(CatalogApiFactory factory) : IClassFixture<CatalogApiFactory>
{
    [Fact]
    public void DbContext_uses_the_configured_connection_string()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        // EF Core adds an Application Name when the connection string has none, so compare the target.
        var expected = new SqlConnectionStringBuilder(factory.ConnectionString);
        var actual = new SqlConnectionStringBuilder(context.Database.GetConnectionString());
        Assert.Equal(expected.DataSource, actual.DataSource);
        Assert.Equal(expected.InitialCatalog, actual.InitialCatalog);
    }

    [Fact]
    public void Migrations_history_table_is_in_dbo()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        var script = context.GetService<IHistoryRepository>().GetCreateIfNotExistsScript();

        Assert.Contains("CREATE TABLE [dbo].[__EFMigrationsHistory]", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Host_does_not_start_without_a_connection_string(string connectionString)
    {
        using var withoutConnectionString = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:CatalogDb", connectionString));

        var exception = Assert.Throws<InvalidOperationException>(() => withoutConnectionString.Services);

        Assert.Contains("'ConnectionStrings:CatalogDb' is not set", exception.Message, StringComparison.Ordinal);
    }
}
