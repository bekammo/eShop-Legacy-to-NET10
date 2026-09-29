using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace eShop.Catalog.Api.Data;

// Migrates the catalog database while the host starts, when Database:MigrateOnStartup is on (ADR-0013).
// StartingAsync runs before any hosted service starts, the server included, so no request reaches an old
// schema. Stopping the host during startup cancels the migration: EF Core rolls back the migration it is
// applying, and keeps the ones it has committed.
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
