using System.Diagnostics;
using System.Text.Json;

namespace eShop.Catalog.Api.IntegrationTests.Logging;

// Reads the CLEF log file of a test host (ADR-0018): one JSON object per line.
internal static class LogFile
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    // The events of a log file, which its host may still hold open for writing.
    public static List<JsonElement> Events(string path)
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

    // The first event that matches, once the host has written it. The request event is written when the request
    // completes, which can be after the client has the response. A line that the host is still writing is read again.
    public static async Task<JsonElement> WaitForEventAsync(string path, Func<JsonElement, bool> match)
    {
        var waited = Stopwatch.StartNew();
        while (waited.Elapsed < WaitTimeout)
        {
            try
            {
                if (File.Exists(path) && Events(path).Where(match).ToList() is [var logEvent, ..])
                {
                    return logEvent;
                }
            }
            catch (JsonException)
            {
            }

            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"No matching event was written to {path} within {WaitTimeout.TotalSeconds} seconds.");
    }

    public static string? String(JsonElement logEvent, string property) =>
        logEvent.TryGetProperty(property, out var value) ? value.GetString() : null;

    // The name of the event, such as the method name of a [LoggerMessage] event.
    public static string? EventName(JsonElement logEvent) =>
        logEvent.TryGetProperty("EventId", out var eventId) && eventId.TryGetProperty("Name", out var name) ? name.GetString() : null;
}
