using System.Text;
using DotNet.Testcontainers.Containers;
using eShop.Catalog.Api.IntegrationTests;
using eShop.Catalog.Api.Tests;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

[assembly: AssemblyFixture(typeof(SqlServerFixture))]

namespace eShop.Catalog.Api.IntegrationTests;

// One SQL Server container for the whole test assembly (ADR-0007). Each test class, or each test that needs one, gets a
// database of its own in it, so they never share data and can run in parallel. The first test that asks for a database
// starts the container, so a run without the tests of the Docker trait needs no Docker (ADR-0031). Building the
// container already needs Docker, so that waits too.
public sealed class SqlServerFixture : IAsyncDisposable
{
    // The trait of every test class that reaches SQL Server. The Docker-free run leaves those classes out with
    // --filter-not-trait "Category=Docker".
    private const string TraitName = "Category";
    private const string TraitValue = "Docker";

    private readonly Lazy<Task<MsSqlContainer>> _container = new(StartAsync);

    public async ValueTask DisposeAsync()
    {
        if (_container.IsValueCreated && _container.Value.IsCompletedSuccessfully)
        {
            await (await _container.Value).DisposeAsync();
        }
    }

    // A connection string for a new database with a unique name. The database does not exist yet:
    // the caller creates it, for example with Migrate() or IRelationalDatabaseCreator.CreateAsync().
    public async Task<string> NewDatabaseAsync(string prefix)
    {
        var container = await ContainerAsync();
        return new SqlConnectionStringBuilder(container.GetConnectionString())
        {
            InitialCatalog = $"{prefix}_{Guid.NewGuid():N}",
        }.ConnectionString;
    }

    // Runs a script with the image's own sqlcmd, as an operator would: -b makes an error end the run
    // with exit code 1, and -C trusts the container's self-signed certificate.
    public async Task<ExecResult> RunSqlcmdAsync(string database, string script, CancellationToken cancellationToken)
    {
        var container = await ContainerAsync();
        var path = $"/tmp/{Guid.NewGuid():N}.sql";
        await container.CopyAsync(Encoding.UTF8.GetBytes(script), path, ct: cancellationToken);
        return await container.ExecAsync(
            ["/opt/mssql-tools18/bin/sqlcmd", "-S", "localhost", "-U", "sa", "-P", MsSqlBuilder.DefaultPassword, "-C", "-d", database, "-b", "-i", path],
            cancellationToken);
    }

    // A test that reaches SQL Server without the trait would make the Docker-free run fail on a machine without
    // Docker, so it fails here, in every run. Before a test runs, as when its class fixture starts, the test class has
    // the traits.
    private Task<MsSqlContainer> ContainerAsync()
    {
        var traits = TestContext.Current.Test?.Traits ?? TestContext.Current.TestClass?.Traits;
        if (traits is null || !traits.TryGetValue(TraitName, out var values) || !values.Contains(TraitValue))
        {
            throw new InvalidOperationException(
                $"This test reaches SQL Server, which runs in Docker: give its class [Trait(\"{TraitName}\", \"{TraitValue}\")], " +
                "so that the Docker-free run leaves it out (ADR-0031).");
        }

        return _container.Value;
    }

    // Not tied to the token of the test that happens to start the container first: every later test waits for it too.
    private static async Task<MsSqlContainer> StartAsync()
    {
        var container = new MsSqlBuilder(SqlServerImage.Name).Build();
        await container.StartAsync();
        return container;
    }
}
