using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace eShop.Catalog.Api.Data;

// StartingAsync runs before any hosted service starts, the server included, so no request reaches an old schema.
// A plain IHostedService or BackgroundService does not guarantee that.
internal sealed class MigrateOnStartupService(IServiceScopeFactory scopeFactory, IOptions<DatabaseOptions> options) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.MigrateOnStartup)
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
