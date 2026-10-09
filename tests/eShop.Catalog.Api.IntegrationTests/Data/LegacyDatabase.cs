using System.IO.Compression;
using System.Text;
using eShop.Catalog.Api.Tests.Legacy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace eShop.Catalog.Api.IntegrationTests.Data;

internal static class LegacyDatabase
{
    public static async Task<string> CreateAsync(SqlServerFixture sqlServer, CancellationToken cancellationToken)
    {
        var connectionString = await CreateEmptyAsync(sqlServer, cancellationToken);
        await using var connection = await OpenAsync(connectionString, cancellationToken);

        await SqlScripts.RunAsync(connection, LegacyFiles.ReadText("schema.sql"), cancellationToken);

        foreach (var (table, column) in new[] { ("CatalogBrand", "Brand"), ("CatalogType", "Type") })
        {
            foreach (var row in LegacySeedData.Table(table))
            {
                await ExecuteAsync(
                    connection,
                    $"SET IDENTITY_INSERT dbo.{table} ON; INSERT INTO dbo.{table} (Id, {column}) VALUES (@id, @name); SET IDENTITY_INSERT dbo.{table} OFF;",
                    cancellationToken,
                    new SqlParameter("@id", (int)row["Id"]!),
                    new SqlParameter("@name", (string)row[column]!));
            }
        }

        foreach (var item in LegacySeedData.Table("Catalog"))
        {
            await ExecuteAsync(
                connection,
                "INSERT INTO dbo.Catalog (Id, Name, Description, Price, PictureFileName, CatalogTypeId, CatalogBrandId, " +
                "AvailableStock, RestockThreshold, MaxStockThreshold, OnReorder) " +
                "VALUES (@Id, @Name, @Description, @Price, @PictureFileName, @CatalogTypeId, @CatalogBrandId, " +
                "@AvailableStock, @RestockThreshold, @MaxStockThreshold, @OnReorder)",
                cancellationToken,
                [.. item.Select(column => new SqlParameter("@" + column.Key, column.Value is null ? DBNull.Value : column.Value.GetValue<object>().ToString()))]);
        }

        var history = LegacySeedData.Table("__MigrationHistory").Single();
        await ExecuteAsync(
            connection,
            "INSERT INTO dbo.__MigrationHistory (MigrationId, ContextKey, Model, ProductVersion) VALUES (@migrationId, @contextKey, @model, @productVersion)",
            cancellationToken,
            new SqlParameter("@migrationId", (string)history["MigrationId"]!),
            new SqlParameter("@contextKey", (string)history["ContextKey"]!),
            new SqlParameter("@model", Ef6Model()),
            new SqlParameter("@productVersion", (string)history["ProductVersion"]!));

        foreach (var (sequence, currentValue) in LegacySeedData.SequenceCurrentValues)
        {
            await DrawUntilAsync(connection, sequence, currentValue, cancellationToken);
        }

        return connectionString;
    }

    public static async Task<string> CreateEmptyAsync(SqlServerFixture sqlServer, CancellationToken cancellationToken)
    {
        var connectionString = await sqlServer.NewDatabaseAsync("legacy");
        await using var context = CatalogDatabase.CreateContext(connectionString);
        await context.GetService<IRelationalDatabaseCreator>().CreateAsync(cancellationToken);
        return connectionString;
    }

    // QUOTED_IDENTIFIER off, as under sqlcmd's defaults; SqlClient turns it on, and the tests would then run
    // baseline.sql under other rules than the documented sqlcmd run.
    public static async Task BaselineAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, "SET QUOTED_IDENTIFIER OFF;", cancellationToken);
        await ExecuteAsync(connection, LegacyFiles.ReadText("baseline.sql"), cancellationToken);
    }

    public static async Task<SqlConnection> OpenAsync(string connectionString, CancellationToken cancellationToken, bool pooling = true)
    {
        var connection = new SqlConnection(new SqlConnectionStringBuilder(connectionString) { Pooling = pooling }.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public static async Task ExecuteAsync(SqlConnection connection, string sql, CancellationToken cancellationToken, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task<T> ScalarAsync<T>(SqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static byte[] Ef6Model()
    {
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal))
        {
            gzip.Write(Encoding.UTF8.GetBytes(LegacyFiles.ReadText("ef6-model.edmx")));
        }

        return compressed.ToArray();
    }

    private static async Task DrawUntilAsync(SqlConnection connection, string sequence, long currentValue, CancellationToken cancellationToken)
    {
        for (var draws = 0; draws < 100; draws++)
        {
            if (await ScalarAsync<long>(connection, $"SELECT NEXT VALUE FOR dbo.{sequence}", cancellationToken) == currentValue)
            {
                return;
            }
        }

        throw new InvalidOperationException($"dbo.{sequence} never reached {currentValue}.");
    }
}
