using Serilog;

namespace eShop.Catalog.Api.Logging;

internal static class LoggingServiceCollectionExtensions
{
    // The path of the log file. The sinks under Serilog:WriteTo are keyed by name rather than listed, so that one
    // setting, such as the environment variable Serilog__WriteTo__File__Args__path, moves the file (ADR-0018).
    internal const string FilePathKey = "Serilog:WriteTo:File:Args:path";

    // Serilog behind ILogger<T>, in place of log4net, configured from the Serilog section (ADR-0018). The logger
    // belongs to the host: the static Log.Logger is left alone, so each host in a test process writes to its own
    // sinks, and disposing one host flushes and closes only its own logger.
    internal static IServiceCollection AddCatalogLogging(this IServiceCollection services) =>
        services.AddSerilog(
            static (provider, logger) => logger.ReadFrom.Configuration(
                WithFilePathUnderContentRoot(
                    provider.GetRequiredService<IConfiguration>(),
                    provider.GetRequiredService<IHostEnvironment>().ContentRootPath)),
            preserveStaticLogger: true);

    // The configuration with a relative log file path resolved against the content root, the folder whose
    // appsettings.json the app reads, as log4net resolved its file against the app root. Serilog would resolve it
    // against the working directory, which differs from the content root for a Windows service and for a host that
    // sets its content root. Serilog expands environment variables such as %TEMP% in the path, so they are expanded
    // first. The result reads through to the configuration, so the levels still follow a reload of the settings.
    internal static IConfiguration WithFilePathUnderContentRoot(IConfiguration configuration, string contentRoot)
    {
        var path = configuration[FilePathKey];
        if (string.IsNullOrWhiteSpace(path))
        {
            return configuration;
        }

        return new ConfigurationBuilder()
            .AddConfiguration(configuration, shouldDisposeConfiguration: false)
            .AddInMemoryCollection([new(FilePathKey, Path.GetFullPath(Environment.ExpandEnvironmentVariables(path), contentRoot))])
            .Build();
    }
}
