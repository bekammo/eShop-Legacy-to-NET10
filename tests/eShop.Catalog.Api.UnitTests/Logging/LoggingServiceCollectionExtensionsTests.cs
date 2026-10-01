using eShop.Catalog.Api.Logging;
using Microsoft.Extensions.Configuration;

namespace eShop.Catalog.Api.UnitTests.Logging;

// Where the log file goes (ADR-0018): a relative path is resolved against the content root, as log4net resolved
// its file against the app root, and not against the working directory.
public sealed class LoggingServiceCollectionExtensionsTests
{
    private const string FilePathKey = "Serilog:WriteTo:File:Args:path";

    // Full, because resolving a path also expands a Windows short name, such as RUNNER~1, in the temporary folder.
    private static readonly string Temp = Path.GetFullPath(Path.GetTempPath());

    private static readonly string ContentRoot = Path.Combine(Temp, "content-root");

    [Fact]
    public void Relative_file_path_is_resolved_against_the_content_root()
    {
        var configuration = Resolve(Settings("logFiles/myapp.log"));

        Assert.Equal(Path.Combine(ContentRoot, "logFiles", "myapp.log"), configuration[FilePathKey]);
    }

    [Fact]
    public void Absolute_file_path_is_kept()
    {
        var path = Path.Combine(Temp, "logs", "myapp.log");

        Assert.Equal(path, Resolve(Settings(path))[FilePathKey]);
    }

    // Serilog expands environment variables in the path. Expanded after the resolution, a variable that holds an
    // absolute path would end up under the content root. The variable's name is the test's own, so no other test
    // reads it.
    [Fact]
    public void Environment_variables_are_expanded_before_the_path_is_resolved()
    {
        var variable = $"ESHOP_CATALOG_LOGGING_TEST_{Guid.NewGuid():N}";
        var directory = Path.Combine(Temp, "logs");
        Environment.SetEnvironmentVariable(variable, directory);
        try
        {
            var configuration = Resolve(Settings($"%{variable}%/myapp.log"));

            Assert.Equal(Path.Combine(directory, "myapp.log"), configuration[FilePathKey]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void Configuration_without_a_file_path_is_returned_as_it_is()
    {
        var settings = Settings(path: null);

        Assert.Same(settings, Resolve(settings));
    }

    // The levels are read from the result, and Serilog changes them when the settings are reloaded.
    [Fact]
    public void Resolved_configuration_reads_through_to_the_settings_and_passes_on_their_reloads()
    {
        var settings = Settings("logFiles/myapp.log");
        var configuration = Resolve(settings);
        var reloadToken = configuration.GetReloadToken();

        settings["Serilog:MinimumLevel:Default"] = "Debug";
        settings.Reload();

        Assert.Equal("Debug", configuration["Serilog:MinimumLevel:Default"]);
        Assert.True(reloadToken.HasChanged);
    }

    private static IConfiguration Resolve(IConfiguration settings) =>
        LoggingServiceCollectionExtensions.WithFilePathUnderContentRoot(settings, ContentRoot);

    private static IConfigurationRoot Settings(string? path) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Serilog:MinimumLevel:Default"] = "Information",
                [FilePathKey] = path,
            })
            .Build();
}
