using eShop.Catalog.Api.Catalog;
using eShop.Catalog.Api.Data;
using eShop.Catalog.Api.Tests.Legacy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace eShop.Catalog.Api.UnitTests.Data;

// The EF Core model against the schema the legacy app creates (docs/legacy/schema.json). Stage 4.2
// checks the database that the migrations create against the same file.
public sealed class CatalogModelTests
{
    // The design-time model keeps what migrations use and the runtime model drops, such as seed data.
    private static readonly IModel Model = CreateDesignTimeModel();

    public static TheoryData<string> LegacyTables => [.. LegacySchema.Tables];

    [Fact]
    public void Model_maps_exactly_the_legacy_catalog_tables() =>
        Assert.Equal(LegacySchema.Tables, EfModelSchema.Tables(Model));

    [Theory]
    [MemberData(nameof(LegacyTables))]
    public void Table_matches_the_legacy_schema(string table) =>
        Assert.Equal(LegacySchema.TableFacts(table), EfModelSchema.TableFacts(Model, table));

    [Fact]
    public void Item_ids_come_from_the_legacy_hilo_sequence()
    {
        Assert.Equal([LegacySchema.SequenceFact(LegacySchema.ItemIdSequence)], EfModelSchema.SequenceFacts(Model));

        var id = Model.FindEntityType(typeof(CatalogItem))!.FindProperty(nameof(CatalogItem.Id))!;
        Assert.Equal(SqlServerValueGenerationStrategy.SequenceHiLo, id.GetValueGenerationStrategy());
        Assert.Equal("catalog_hilo", id.GetHiLoSequenceName());
    }

    [Fact]
    public void Brands_are_seeded_with_their_legacy_ids() =>
        Assert.Equal(LegacySeedData.Brands, SeedRows<CatalogBrand>(nameof(CatalogBrand.Brand)));

    [Fact]
    public void Types_are_seeded_with_their_legacy_ids() =>
        Assert.Equal(LegacySeedData.Types, SeedRows<CatalogType>(nameof(CatalogType.Type)));

    private static IReadOnlyList<string> SeedRows<TEntity>(string nameProperty) =>
        [.. Model.FindEntityType(typeof(TEntity))!.GetSeedData()
            .Select(row => ((int)row["Id"]!, (string)row[nameProperty]!))
            .OrderBy(row => row.Item1)
            .Select(row => LegacySeedData.Row(row.Item1, row.Item2))];

    private static IModel CreateDesignTimeModel()
    {
        // The app's SQL Server options. Building the model needs the provider, not a connection.
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        options.UseCatalogSqlServer();
        using var context = new CatalogDbContext(options.Options);
        return context.GetService<IDesignTimeModel>().Model;
    }
}
