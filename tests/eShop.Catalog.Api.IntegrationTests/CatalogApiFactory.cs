using eShop.Catalog.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace eShop.Catalog.Api.IntegrationTests;

public class CatalogApiFactory(SqlServerFixture sqlServer) : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string EnvironmentName = "Testing";

    private int _hosts;

    public string ConnectionString { get; private set; } = "";

    public string LogFilePath { get; } =
        Path.Combine(Directory.CreateTempSubdirectory("eShop.Catalog.Api.IntegrationTests-").FullName, "myapp.log");

    public async Task UseNewDatabaseAsync() => ConnectionString = await sqlServer.NewDatabaseAsync("catalog");

    public virtual async ValueTask InitializeAsync()
    {
        await UseNewDatabaseAsync();
        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    // After base.DisposeAsync, which closes the hosts' log files. A host that failed to start is never disposed, and
    // on Windows its open file blocks the delete, so the IOException is ignored.
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

    // Not UseSetting: the factory passes it to Program as command-line arguments with each parent section as an empty
    // value, and Serilog takes the empty Serilog:WriteTo:File for a sink name and drops the file sink.
    public static IWebHostBuilder UseLogFile(IWebHostBuilder builder, string path) =>
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection([new("Serilog:WriteTo:File:Args:path", path)]));

    // UseSetting for the connection string and the mode: Program.cs reads them while it registers services, before a
    // ConfigureAppConfiguration source applies. Each later host gets a log file of its own: two hosts sharing one file
    // overwrite each other's lines on Linux.
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
