using Microsoft.AspNetCore.Builder;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace eShop.Catalog.Api.IntegrationTests.Configuration;

public sealed class ConfigurationTests(MockModeCatalogApiFactory factory) : IClassFixture<MockModeCatalogApiFactory>
{
    private string ContentRoot => factory.Services.GetRequiredService<IHostEnvironment>().ContentRootPath;

    [Fact]
    public void Testing_host_gets_no_connection_string_from_settings_files()
    {
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
            Args = ["--hostBuilder:reloadConfigOnChange=false"],
        }).Configuration;

        Assert.Contains(((IConfigurationRoot)configuration).Providers, IsUserSecrets);
    }

    [Fact]
    public void Development_settings_point_at_a_LocalDB_database_of_their_own_without_MARS()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(ContentRoot)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: false)
            .Build();

        var connectionString = new SqlConnectionStringBuilder(configuration.GetConnectionString("CatalogDb"));

        Assert.Equal(@"(localdb)\MSSQLLocalDB", connectionString.DataSource);
        Assert.Equal("eShopCatalog", connectionString.InitialCatalog);
        Assert.False(connectionString.MultipleActiveResultSets);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Committed_settings_turn_migrate_on_startup_on_in_Development_only(bool development, bool expected)
    {
        var files = new ConfigurationBuilder()
            .SetBasePath(ContentRoot)
            .AddJsonFile("appsettings.json", optional: false);
        if (development)
        {
            files.AddJsonFile("appsettings.Development.json", optional: false);
        }

        var configuration = files.Build();

        Assert.Equal(expected, configuration.GetValue<bool?>("Database:MigrateOnStartup"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Committed_settings_keep_mock_mode_off(bool development)
    {
        var files = new ConfigurationBuilder()
            .SetBasePath(ContentRoot)
            .AddJsonFile("appsettings.json", optional: false);
        if (development)
        {
            files.AddJsonFile("appsettings.Development.json", optional: false);
        }

        var configuration = files.Build();

        Assert.False(configuration.GetValue<bool?>("Catalog:UseMockData"));
    }

    [Fact]
    public void Committed_settings_serve_the_pictures_from_the_projects_Pics_folder()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(ContentRoot)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        Assert.Equal("Pics", configuration["Catalog:PicturesPath"]);
    }

    private static bool IsUserSecrets(IConfigurationProvider provider) =>
        provider is JsonConfigurationProvider { Source.Path: "secrets.json" };
}
