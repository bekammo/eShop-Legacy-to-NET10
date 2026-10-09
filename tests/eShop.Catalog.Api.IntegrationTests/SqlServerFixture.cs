using System.Text;
using DotNet.Testcontainers.Containers;
using eShop.Catalog.Api.IntegrationTests;
using eShop.Catalog.Api.Tests;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

[assembly: AssemblyFixture(typeof(SqlServerFixture))]

namespace eShop.Catalog.Api.IntegrationTests;

public sealed class SqlServerFixture : IAsyncDisposable
{
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

    public async Task<string> NewDatabaseAsync(string prefix)
    {
        var container = await ContainerAsync();
        return new SqlConnectionStringBuilder(container.GetConnectionString())
        {
            InitialCatalog = $"{prefix}_{Guid.NewGuid():N}",
        }.ConnectionString;
    }

    public async Task<ExecResult> RunSqlcmdAsync(string database, string script, CancellationToken cancellationToken)
    {
        var container = await ContainerAsync();
        var path = $"/tmp/{Guid.NewGuid():N}.sql";
        await container.CopyAsync(Encoding.UTF8.GetBytes(script), path, ct: cancellationToken);
        return await container.ExecAsync(
            ["/opt/mssql-tools18/bin/sqlcmd", "-S", "localhost", "-U", "sa", "-P", MsSqlBuilder.DefaultPassword, "-C", "-d", database, "-b", "-i", path],
            cancellationToken);
    }

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

    // No cancellation token: the container start is shared, and if the first test's token cancelled it, every later
    // test would await the same cancelled task.
    private static async Task<MsSqlContainer> StartAsync()
    {
        var container = new MsSqlBuilder(SqlServerImage.Name).Build();
        await container.StartAsync();
        return container;
    }
}
