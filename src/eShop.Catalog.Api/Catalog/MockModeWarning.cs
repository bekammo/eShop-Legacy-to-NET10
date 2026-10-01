namespace eShop.Catalog.Api.Catalog;

// Warns once, when the host starts, that the catalog is served from memory (ADR-0019). A host in mock mode needs no
// database and is ready at once (ADR-0017), so nothing else would show a deployment that turned it on by mistake.
internal sealed partial class MockModeWarning(ILogger<MockModeWarning> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        MockModeIsOn(logger);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Warning, Message =
        "Mock mode is on (Catalog:UseMockData): the catalog is served from memory, starting with the sample data, " +
        "and every change is lost when the process stops")]
    private static partial void MockModeIsOn(ILogger logger);
}
