using System.Diagnostics;
using System.Text.Json;

namespace eShop.Catalog.Api.IntegrationTests.Logging;

internal static class LogFile
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    // FileShare.ReadWrite: the host still holds the file open for writing, so File.ReadAllLines would fail on Windows.
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

    // Callers wait here rather than read Events: the request event can be written after the client has the response.
    // The empty catch is deliberate: a line the host is still writing does not parse yet, and is read again.
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

    public static string? EventName(JsonElement logEvent) =>
        logEvent.TryGetProperty("EventId", out var eventId) && eventId.TryGetProperty("Name", out var name) ? name.GetString() : null;
}
