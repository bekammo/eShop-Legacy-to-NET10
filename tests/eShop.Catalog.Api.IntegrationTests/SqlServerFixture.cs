using eShop.Catalog.Api.IntegrationTests;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

[assembly: AssemblyFixture(typeof(SqlServerFixture))]

namespace eShop.Catalog.Api.IntegrationTests;

// One SQL Server container for the whole test assembly (ADR-0007). Each test class gets a database of
// its own in it, so classes never share data and can run in parallel.
public sealed class SqlServerFixture : IAsyncLifetime
{
    // Pinned, so that every machine and CI run the same server and an upgrade is a deliberate change
    // (ADR-0011). SQL Server 2025 is the version the legacy capture ran on.
    public const string Image = "mcr.microsoft.com/mssql/server:2025-CU9-ubuntu-24.04";

    private readonly MsSqlContainer _container = new MsSqlBuilder(Image).Build();

    public async ValueTask InitializeAsync() => await _container.StartAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    // A connection string for a new database with a unique name. The database does not exist yet:
    // the caller creates it, for example with Migrate() or IRelationalDatabaseCreator.CreateAsync().
    public string NewDatabase(string prefix) =>
        new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = $"{prefix}_{Guid.NewGuid():N}",
        }.ConnectionString;
}
