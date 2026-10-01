using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Context;
using Serilog.Events;

namespace eShop.Catalog.Api.IntegrationTests.Logging;

// Serilog in place of log4net (ADR-0018): the committed sinks, the log file's limits, and what the host writes.
public sealed partial class LoggingTests(CatalogApiFactory factory) : IClassFixture<CatalogApiFactory>
{
    private const int MiB = 1024 * 1024;

    private string ContentRoot => factory.Services.GetRequiredService<IHostEnvironment>().ContentRootPath;

    // Only the committed file, as in ConfigurationTests: an override must not fail the test.
    [Fact]
    public void Committed_settings_log_from_Information_to_the_console_and_to_the_log4net_file()
    {
        var serilog = CommittedSettings().GetSection("Serilog");

        Assert.Equal("Information", serilog["MinimumLevel:Default"]);
        // EF Core logs the SQL of every command at Information. The legacy app logged no SQL.
        Assert.Equal([("Microsoft.EntityFrameworkCore.Database.Command", "Warning")],
            serilog.GetSection("MinimumLevel:Override").GetChildren().Select(static level => (level.Key, level.Value)));
        Assert.Equal("Console", serilog["WriteTo:Console:Name"]);
        Assert.Equal("File", serilog["WriteTo:File:Name"]);
        Assert.Equal("FromLogContext", Assert.Single(serilog.GetSection("Enrich").GetChildren()).Value);
        // log4net's file and limits: logFiles\myapp.log, rollingStyle Size, maximumFileSize 10MB,
        // maxSizeRollBackups 5 plus the active file. No rolling by date.
        var file = serilog.GetSection("WriteTo:File:Args");
        Assert.Equal("logFiles/myapp.log", file["path"]);
        Assert.True(file.GetValue<bool>("rollOnFileSizeLimit"));
        Assert.Equal(10 * MiB, file.GetValue<long>("fileSizeLimitBytes"));
        Assert.Equal(6, file.GetValue<int>("retainedFileCountLimit"));
        Assert.Null(file["rollingInterval"]);
        Assert.Equal("Serilog.Formatting.Compact.CompactJsonFormatter, Serilog.Formatting.Compact", file["formatter"]);
    }

    // The committed file sink, alone and in a directory of the test's own, written past its seventh file.
    [Fact]
    public async Task Log_file_rolls_at_10_MiB_and_keeps_the_6_newest_files()
    {
        var directory = Directory.CreateTempSubdirectory("eShop.Catalog.Api.IntegrationTests-");
        try
        {
            // Small enough events to tell 10 MB (10,000,000 bytes) from log4net's 10MB (10 MiB).
            var payload = new string('x', 64 * 1024);
            var logger = new LoggerConfiguration()
                .ReadFrom.Configuration(CommittedFileSinkOnly(Path.Combine(directory.FullName, "myapp.log")))
                .CreateLogger();
            await using (logger)
            {
                // Two events into the seventh file: the first one rolls to it, the second one is written to it. At
                // most eight files' worth, so that a sink that does not roll fails the test rather than hanging it.
                var seventhFile = Path.Combine(directory.FullName, "myapp_006.log");
                for (var events = 0; !File.Exists(seventhFile) && events < 8 * 10 * MiB / payload.Length; events++)
                {
                    logger.Information("{Payload}", payload);
                }

                logger.Information("{Payload}", payload);
            }

            var files = directory.GetFiles().OrderBy(static file => file.Name, StringComparer.Ordinal).ToArray();
            Assert.Equal(["myapp_001.log", "myapp_002.log", "myapp_003.log", "myapp_004.log", "myapp_005.log", "myapp_006.log"],
                files.Select(static file => file.Name));
            // A file takes events while it is below the limit, so a full one ends less than one event above it. Every
            // event has the same length: the payload, and a timestamp of fixed width.
            var eventLength = (await File.ReadAllLinesAsync(files[0].FullName, TestContext.Current.CancellationToken))[0].Length + Environment.NewLine.Length;
            Assert.All(files[..^1], file => Assert.InRange(file.Length, 10 * MiB, (10 * MiB) + eventLength - 1));
            Assert.Equal(2, (await File.ReadAllLinesAsync(files[^1].FullName, TestContext.Current.CancellationToken)).Length);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Host_writes_each_event_as_a_CLEF_line_with_its_trace_span_and_log_context()
    {
        var logger = factory.Services.GetRequiredService<ILogger<LoggingTests>>();
        var probe = Guid.NewGuid().ToString("N");

        using var activity = new Activity("logging-test").Start();
        using (LogContext.PushProperty("Scenario", "clef"))
        {
            LogProbe(logger, probe);
        }

        var logEvent = Assert.Single(Events(factory.LogFilePath), logEvent => HasProbe(logEvent, probe));
        Assert.Equal("Logging test probe {Probe}", logEvent.GetProperty("@mt").GetString());
        Assert.Equal(activity.TraceId.ToHexString(), logEvent.GetProperty("@tr").GetString());
        Assert.Equal(activity.SpanId.ToHexString(), logEvent.GetProperty("@sp").GetString());
        Assert.Equal(typeof(LoggingTests).FullName, logEvent.GetProperty("SourceContext").GetString());
        Assert.Equal("clef", logEvent.GetProperty("Scenario").GetString());
        // CLEF leaves out the level when it is Information.
        Assert.False(logEvent.TryGetProperty("@l", out _));
    }

    // Each host owns its logger. With the static Log.Logger set by every host, the loggers that a host creates
    // would write to the sinks of the last host to start, and the first host to stop would close them.
    [Fact]
    public async Task Each_host_writes_to_its_own_sinks_and_leaves_the_static_logger_silent()
    {
        var directory = Directory.CreateTempSubdirectory("eShop.Catalog.Api.IntegrationTests-");
        try
        {
            string[] paths = [Path.Combine(directory.FullName, "first.log"), Path.Combine(directory.FullName, "second.log")];
            string[] probes = [Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N")];
            await using (var first = factory.WithWebHostBuilder(builder => CatalogApiFactory.UseLogFile(builder, paths[0])))
            await using (var second = factory.WithWebHostBuilder(builder => CatalogApiFactory.UseLogFile(builder, paths[1])))
            {
                _ = first.Services;
                _ = second.Services;

                Assert.False(Log.Logger.IsEnabled(LogEventLevel.Fatal));
                var firstLogger = first.Services.GetRequiredService<ILogger<LoggingTests>>();
                var secondLogger = second.Services.GetRequiredService<ILogger<LoggingTests>>();
                LogProbe(firstLogger, probes[0]);
                LogProbe(secondLogger, probes[1]);
            }

            Assert.Equal([probes[0]], ProbesIn(paths[0], probes));
            Assert.Equal([probes[1]], ProbesIn(paths[1], probes));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    // log4net resolved its file against the app root. The test host runs from the test's output folder, which is
    // its working directory, with the content root in a directory of its own.
    [Fact]
    public async Task Relative_log_file_path_is_resolved_against_the_content_root()
    {
        var contentRoot = Directory.CreateTempSubdirectory("eShop.Catalog.Api.IntegrationTests-");
        try
        {
            File.Copy(Path.Combine(ContentRoot, "appsettings.json"), Path.Combine(contentRoot.FullName, "appsettings.json"));
            var probe = Guid.NewGuid().ToString("N");
            await using (var host = factory.WithWebHostBuilder(builder =>
                CatalogApiFactory.UseLogFile(builder.UseContentRoot(contentRoot.FullName), "logFiles/myapp.log")))
            {
                var logger = host.Services.GetRequiredService<ILogger<LoggingTests>>();
                LogProbe(logger, probe);
            }

            Assert.Single(Events(Path.Combine(contentRoot.FullName, "logFiles", "myapp.log")), logEvent => HasProbe(logEvent, probe));
        }
        finally
        {
            contentRoot.Delete(recursive: true);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Logging test probe {Probe}")]
    private static partial void LogProbe(Microsoft.Extensions.Logging.ILogger logger, string probe);

    private IConfigurationRoot CommittedSettings() =>
        new ConfigurationBuilder()
            .SetBasePath(ContentRoot)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

    // The committed Serilog settings without the console sink, which would print every event, writing to this path.
    private IConfigurationRoot CommittedFileSinkOnly(string path) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(CommittedSettings().AsEnumerable()
                .Where(static setting => setting.Key.StartsWith("Serilog:WriteTo:File:", StringComparison.Ordinal)
                    || setting.Key.StartsWith("Serilog:Using:", StringComparison.Ordinal)))
            .AddInMemoryCollection([new("Serilog:WriteTo:File:Args:path", path)])
            .Build();

    // The events of a log file, which its host may still hold open for writing.
    private static List<JsonElement> Events(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(file);
        var events = new List<JsonElement>();
        while (reader.ReadLine() is { } line)
        {
            using var document = JsonDocument.Parse(line);
            events.Add(document.RootElement.Clone());
        }

        return events;
    }

    private static bool HasProbe(JsonElement logEvent, string probe) =>
        logEvent.TryGetProperty("Probe", out var value) && value.GetString() == probe;

    // Which of the probes a log file holds, if it exists.
    private static List<string> ProbesIn(string path, string[] probes) =>
        File.Exists(path) ? [.. probes.Where(probe => Events(path).Any(logEvent => HasProbe(logEvent, probe)))] : [];
}
