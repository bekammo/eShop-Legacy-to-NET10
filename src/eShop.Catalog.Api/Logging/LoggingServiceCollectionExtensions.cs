using Serilog;

namespace eShop.Catalog.Api.Logging;

internal static class LoggingServiceCollectionExtensions
{
    internal const string FilePathKey = "Serilog:WriteTo:File:Args:path";

    internal static IServiceCollection AddCatalogLogging(this IServiceCollection services) =>
        services.AddSerilog(
            static (provider, logger) => logger.ReadFrom.Configuration(
                WithFilePathUnderContentRoot(
                    provider.GetRequiredService<IConfiguration>(),
                    provider.GetRequiredService<IHostEnvironment>().ContentRootPath)),
            preserveStaticLogger: true);

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
