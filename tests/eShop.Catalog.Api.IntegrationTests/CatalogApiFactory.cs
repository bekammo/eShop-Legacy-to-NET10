using eShop.Catalog.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace eShop.Catalog.Api.IntegrationTests;

// Hosts the API in memory for the integration tests (ADR-0007). Each test class gets its own instance through
// IClassFixture<CatalogApiFactory>, and with it a database of its own in the shared SQL Server container, migrated
// before the class's first test. Such a class needs Docker, and has the Docker trait (ADR-0031).
// MockModeCatalogApiFactory hosts the API without a database.
public class CatalogApiFactory(SqlServerFixture sqlServer) : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Not Development: user secrets and Development-only features stay off unless a test opts in.
    public const string EnvironmentName = "Testing";

    // The hosts built so far: this factory's own, and those of WithWebHostBuilder, which runs ConfigureWebHost too.
    private int _hosts;

    // The Testing environment has no connection string of its own (ADR-0009): this factory's database, once
    // UseNewDatabaseAsync has named it. Until then a host in database mode does not start.
    public string ConnectionString { get; private set; } = "";

    // The log file of this factory's own host, in a temporary directory of its own (ADR-0018). The committed path is
    // relative to the content root, which is the API's source folder here, and every host would share it.
    public string LogFilePath { get; } =
        Path.Combine(Directory.CreateTempSubdirectory("eShop.Catalog.Api.IntegrationTests-").FullName, "myapp.log");

    // Names a new database in the shared container, which a host or a migration creates, for the hosts built from now
    // on. xUnit does it for a class fixture, and a test that starts hosts of its own from a factory that xUnit does not
    // initialize does it itself.
    public async Task UseNewDatabaseAsync() => ConnectionString = await sqlServer.NewDatabaseAsync("catalog");

    public virtual async ValueTask InitializeAsync()
    {
        await UseNewDatabaseAsync();
        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    // Disposing the hosts closes their log files. A host that failed to start is never disposed, and on Windows
    // its open file keeps the directory, which is then left in the temporary folder.
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(Path.GetDirectoryName(LogFilePath)!, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // Points the host's log file at a path, with a configuration source rather than UseSetting. WebApplicationFactory
    // passes the UseSetting values to Program as command-line arguments, each parent section of the key included as
    // an empty value, and Serilog would take the empty Serilog:WriteTo:File for the name of a sink, and leave the
    // file sink out. The logger reads its settings when the container is built, after the source is added.
    public static IWebHostBuilder UseLogFile(IWebHostBuilder builder, string path) =>
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection([new("Serilog:WriteTo:File:Args:path", path)]));

    // WebApplicationFactory passes host settings to Program as command-line arguments, which
    // CreateBuilder reads first, so Program.cs sees them while it registers services; they also
    // override environment variables. ConfigureAppConfiguration would apply only after that.
    // Database mode, whatever a developer's environment variables say (ADR-0017): a test that wants mock mode sets
    // it with UseSetting, which comes later and wins.
    // The first host, which InitializeAsync builds, writes to LogFilePath, and every later host to a file of its own
    // beside it: two hosts writing to one file overwrite each other's lines on Linux, and on Windows the second one
    // moves to another file. A test can still choose the path with UseLogFile, which comes later and wins.
    // Every host accepts the tests' access tokens (ADR-0034), whose settings the Bearer scheme reads on the host's first
    // request, so a configuration source is enough.
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var host = Interlocked.Increment(ref _hosts);
        var logFilePath = host == 1 ? LogFilePath : Path.Combine(Path.GetDirectoryName(LogFilePath)!, $"host-{host}.log");
        UseLogFile(
            builder.UseEnvironment(EnvironmentName)
                .UseSetting("ConnectionStrings:CatalogDb", ConnectionString)
                .UseSetting("Catalog:UseMockData", "false")
                .ConfigureAppConfiguration(static (_, configuration) => configuration.AddInMemoryCollection(AccessTokens.Settings)),
            logFilePath);
    }
}
