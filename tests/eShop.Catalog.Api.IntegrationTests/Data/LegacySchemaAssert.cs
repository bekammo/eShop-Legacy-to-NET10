using eShop.Catalog.Api.Tests.Legacy;
using Microsoft.Data.SqlClient;

namespace eShop.Catalog.Api.IntegrationTests.Data;

// Every schema check against docs/legacy/schema.json in one call, for tests that build a database
// some other way than CatalogApiFactory does. MigrationTests makes the same checks one by one.
internal static class LegacySchemaAssert
{
    public static async Task HasTheLegacySchemaAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        Assert.Equal(LegacySchema.Tables, await SqlServerSchema.TablesAsync(connection, cancellationToken));
        foreach (var table in LegacySchema.Tables)
        {
            Assert.Equal(LegacySchema.TableFacts(table), await SqlServerSchema.TableFactsAsync(connection, table, cancellationToken));
        }

        Assert.Equal(
            [LegacySchema.SequenceFact(LegacySchema.ItemIdSequence)],
            await SqlServerSchema.SequenceFactsAsync(connection, cancellationToken));
        Assert.Equal(LegacySchema.ObjectCountFacts(), await SqlServerSchema.ObjectCountFactsAsync(connection, cancellationToken));
        Assert.Empty(await SqlServerSchema.ObjectsOutsideSysObjectsAsync(connection, cancellationToken));
        Assert.Equal(MigrationsHistoryTable.Facts, await SqlServerSchema.TableFactsAsync(connection, MigrationsHistoryTable.Name, cancellationToken));
    }
}
