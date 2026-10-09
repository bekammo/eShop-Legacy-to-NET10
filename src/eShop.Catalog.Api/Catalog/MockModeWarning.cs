namespace eShop.Catalog.Api.Catalog;

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
