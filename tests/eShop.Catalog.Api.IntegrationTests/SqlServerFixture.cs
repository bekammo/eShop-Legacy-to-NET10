using System.Text;
using DotNet.Testcontainers.Containers;
using eShop.Catalog.Api.IntegrationTests;
using eShop.Catalog.Api.Tests;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

[assembly: AssemblyFixture(typeof(SqlServerFixture))]

namespace eShop.Catalog.Api.IntegrationTests;

// One SQL Server container for the whole test assembly (ADR-0007). Each test class, or each test that
// needs one, gets a database of its own in it, so they never share data and can run in parallel.
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder(SqlServerImage.Name).Build();

    public async ValueTask InitializeAsync() => await _container.StartAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    // A connection string for a new database with a unique name. The database does not exist yet:
    // the caller creates it, for example with Migrate() or IRelationalDatabaseCreator.CreateAsync().
    public string NewDatabase(string prefix) =>
        new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = $"{prefix}_{Guid.NewGuid():N}",
        }.ConnectionString;

    // Runs a script with the image's own sqlcmd, as an operator would: -b makes an error end the run
    // with exit code 1, and -C trusts the container's self-signed certificate.
    public async Task<ExecResult> RunSqlcmdAsync(string database, string script, CancellationToken cancellationToken)
    {
        var path = $"/tmp/{Guid.NewGuid():N}.sql";
        await _container.CopyAsync(Encoding.UTF8.GetBytes(script), path, ct: cancellationToken);
        return await _container.ExecAsync(
            ["/opt/mssql-tools18/bin/sqlcmd", "-S", "localhost", "-U", "sa", "-P", MsSqlBuilder.DefaultPassword, "-C", "-d", database, "-b", "-i", path],
            cancellationToken);
    }
}
