using System.IO.Compression;
using System.Text;
using eShop.Catalog.Api.Tests.Legacy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace eShop.Catalog.Api.IntegrationTests.Data;

// Legacy databases for the Stage 4.3 baseline tests, and the baseline itself.
internal static class LegacyDatabase
{
    // What the legacy app leaves behind on its first start (docs/legacy): the schema from schema.sql,
    // the rows from seed-data.json, EF6's history row with the model in ef6-model.edmx, and every
    // sequence drawn as far as the legacy seeding drew it. The database is created as EF6 and EF Core
    // create theirs, with READ_COMMITTED_SNAPSHOT on.
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

    // A database that exists but has nothing in it, created the same way.
    public static async Task<string> CreateEmptyAsync(SqlServerFixture sqlServer, CancellationToken cancellationToken)
    {
        var connectionString = sqlServer.NewDatabase("legacy");
        await using var context = CatalogDatabase.CreateContext(connectionString);
        await context.GetService<IRelationalDatabaseCreator>().CreateAsync(cancellationToken);
        return connectionString;
    }

    // Runs docs/legacy/baseline.sql as sqlcmd does by default, with QUOTED_IDENTIFIER off.
    public static async Task BaselineAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, "SET QUOTED_IDENTIFIER OFF;", cancellationToken);
        await ExecuteAsync(connection, LegacyFiles.ReadText("baseline.sql"), cancellationToken);
    }

    // A connection that changes its session (EXECUTE AS, SET options) must not be pooled, so that the
    // change ends with it instead of reaching the next user of the pooled connection.
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

    // EF6 stores its model gzip-compressed. The capture decompressed it into ef6-model.edmx.
    private static byte[] Ef6Model()
    {
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal))
        {
            gzip.Write(Encoding.UTF8.GetBytes(LegacyFiles.ReadText("ef6-model.edmx")));
        }

        return compressed.ToArray();
    }

    // Draws values as the legacy seeding did, until the sequence's current value is the recorded one.
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
