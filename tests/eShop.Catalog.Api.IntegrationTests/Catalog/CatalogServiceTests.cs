using System.Data.Common;
using eShop.Catalog.Api.Catalog;
using eShop.Catalog.Api.Data;
using eShop.Catalog.Api.Tests.Catalog;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace eShop.Catalog.Api.IntegrationTests.Catalog;

[Trait("Category", "Docker")]
public sealed class CatalogServiceTests : CatalogServiceContractTests, IClassFixture<CatalogApiFactory>, IAsyncLifetime
{
    private readonly string _connectionString;
    private readonly AsyncServiceScope _scope;
    private readonly CatalogDbContext _context;

    public CatalogServiceTests(CatalogApiFactory factory)
    {
        _connectionString = factory.ConnectionString;
        _scope = factory.Services.CreateAsyncScope();
        _context = _scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Service = new CatalogService(_context);
    }

    private protected override ICatalogService Service { get; }

    public async ValueTask InitializeAsync() => await _context.Database.BeginTransactionAsync(CancellationToken);

    public ValueTask DisposeAsync() => _scope.DisposeAsync();

    [Fact]
    public async Task Service_leaves_nothing_tracked()
    {
        await Service.GetCatalogItemsPaginatedAsync(12, 0, CancellationToken);
        await Service.FindCatalogItemAsync(1, CancellationToken);
        await Service.GetCatalogBrandsAsync(CancellationToken);
        await Service.FindCatalogBrandAsync(1, CancellationToken);
        await Service.GetCatalogTypesAsync(CancellationToken);
        var created = await Service.CreateCatalogItemAsync(NewFields("Created"), CancellationToken);
        await Service.UpdateCatalogItemAsync(created.Id, NewFields("Updated"), CancellationToken);
        await Service.RemoveCatalogItemAsync(created.Id, CancellationToken);
        var refused = NewFields("Refused") with { CatalogBrandId = 6 };
        await Assert.ThrowsAsync<DbUpdateException>(() => Service.CreateCatalogItemAsync(refused, CancellationToken));
        await Assert.ThrowsAsync<SqlException>(() => Service.UpdateCatalogItemAsync(1, refused, CancellationToken));

        Assert.Empty(_context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Brand_lookup_asks_the_database_for_the_one_brand()
    {
        var commands = await CommandsAsync(service => service.FindCatalogBrandAsync(2, CancellationToken));

        var command = Assert.Single(commands);
        Assert.Contains("TOP(1)", command, StringComparison.Ordinal);
        Assert.Contains("WHERE", command, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Paging_reads_only_the_rows_of_the_page_in_id_order()
    {
        var commands = await CommandsAsync(service => service.GetCatalogItemsPaginatedAsync(4, 1, CancellationToken));

        Assert.Equal(2, commands.Count);
        Assert.Contains("COUNT_BIG(*)", commands[0], StringComparison.Ordinal);
        Assert.Contains("ORDER BY [c].[Id]", commands[1], StringComparison.Ordinal);
        Assert.Contains("OFFSET", commands[1], StringComparison.Ordinal);
        Assert.Contains("FETCH NEXT", commands[1], StringComparison.Ordinal);
    }

    // Asserts on the SQL, not the rows: SQL Server returns small tables in key order without ORDER BY too, so a check
    // of the rows would still pass with the ordering removed.
    [Fact]
    public async Task Brands_and_types_are_read_in_id_order()
    {
        var commands = await CommandsAsync(async service =>
        {
            await service.GetCatalogBrandsAsync(CancellationToken);
            await service.GetCatalogTypesAsync(CancellationToken);
        });

        Assert.Equal(2, commands.Count);
        Assert.All(commands, command => Assert.Contains("ORDER BY [c].[Id]", command, StringComparison.Ordinal));
    }

    private async Task<IReadOnlyList<string>> CommandsAsync(Func<ICatalogService, Task> use)
    {
        var recorder = new CommandRecorder();
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        options.UseCatalogSqlServer(_connectionString).AddInterceptors(recorder);
        await using var context = new CatalogDbContext(options.Options);

        await use(new CatalogService(context));

        return recorder.Commands;
    }

    private sealed class CommandRecorder : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
