using System.Data.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace eShop.Catalog.Api.IntegrationTests.Configuration;

public sealed class ConfigurationTests(CatalogApiFactory factory) : IClassFixture<CatalogApiFactory>
{
    private string ContentRoot => factory.Services.GetRequiredService<IHostEnvironment>().ContentRootPath;

    [Fact]
    public void Testing_host_gets_no_connection_string_from_settings_files()
    {
        // Only file sources: an environment variable or a value that the factory sets in code
        // is allowed to supply the connection string.
        var configuration = (IConfigurationRoot)factory.Services.GetRequiredService<IConfiguration>();
        var settingsFiles = configuration.Providers.OfType<FileConfigurationProvider>().ToList();

        Assert.Contains(settingsFiles, static file => file.Source.Path == "appsettings.json");
        Assert.All(settingsFiles, static file =>
            Assert.False(file.TryGet("ConnectionStrings:CatalogDb", out _), file.Source.Path));
    }

    [Fact]
    public void Testing_host_reads_no_user_secrets()
    {
        var configuration = (IConfigurationRoot)factory.Services.GetRequiredService<IConfiguration>();

        Assert.DoesNotContain(configuration.Providers, IsUserSecrets);
    }

    [Fact]
    public void Development_host_reads_user_secrets()
    {
        using var configuration = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            EnvironmentName = Environments.Development,
            ContentRootPath = ContentRoot,
            // The test only inspects the sources, so no file watchers.
            Args = ["--hostBuilder:reloadConfigOnChange=false"],
        }).Configuration;

        Assert.Contains(((IConfigurationRoot)configuration).Providers, IsUserSecrets);
    }

    [Fact]
    public void Development_settings_point_at_a_LocalDB_database_of_their_own_without_MARS()
    {
        // Only the committed files: a developer may override the value with user secrets or an
        // environment variable, and that must not fail the test.
        var configuration = new ConfigurationBuilder()
            .SetBasePath(ContentRoot)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: false)
            .Build();

        // DbConnectionStringBuilder matches keywords literally, so this checks the keywords the file
        // uses. SqlConnectionStringBuilder, which also knows their synonyms, arrives with SqlClient in 4.1.
        var connectionString = new DbConnectionStringBuilder { ConnectionString = configuration.GetConnectionString("CatalogDb") };

        Assert.Equal(@"(localdb)\MSSQLLocalDB", connectionString["Data Source"]);
        Assert.Equal("eShopCatalog", connectionString["Initial Catalog"]);
        Assert.False(connectionString.ContainsKey("MultipleActiveResultSets"));
    }

    // User secrets are a JSON source for secrets.json, added whether or not the file exists yet.
    private static bool IsUserSecrets(IConfigurationProvider provider) =>
        provider is JsonConfigurationProvider { Source.Path: "secrets.json" };
}
