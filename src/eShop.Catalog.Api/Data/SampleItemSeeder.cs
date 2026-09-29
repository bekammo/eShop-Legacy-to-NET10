using eShop.Catalog.Api.Catalog;
using Microsoft.EntityFrameworkCore;

namespace eShop.Catalog.Api.Data;

// Seeds the legacy app's 12 sample items into a new database (ADR-0013). EF Core calls it at the end of
// every Migrate and dotnet ef database update, after it has committed the migrations and while it still
// holds the migrations lock, whether or not anything was migrated. So it decides for itself, and seeds only
// a database that nobody has used yet. Brands and types are reference data in the migrations (ADR-0010).
// The items are not, because their IDs come from HiLo.
internal static class SampleItemSeeder
{
    // dotnet ef database update migrates synchronously; the app and the tests migrate asynchronously.
    public static void Seed(DbContext context, bool storeManagementPerformed)
    {
        if (context.Database.GetPendingMigrations().Any()
            || context.Set<CatalogItem>().Any()
            || !context.Database.SqlQuery<bool>(IsUnusedQuery()).Single())
        {
            return;
        }

        var items = CreateItems();
        for (var index = 0; index < items.Count; index++)
        {
            context.Set<CatalogItem>().Add(items[index]);
            EnsureIdFromTheNewSequence(context, items, index);
        }

        context.SaveChanges();
        StopTracking(context, items);
    }

    public static async Task SeedAsync(DbContext context, bool storeManagementPerformed, CancellationToken cancellationToken)
    {
        if ((await context.Database.GetPendingMigrationsAsync(cancellationToken)).Any()
            || await context.Set<CatalogItem>().AnyAsync(cancellationToken)
            || !await context.Database.SqlQuery<bool>(IsUnusedQuery()).SingleAsync(cancellationToken))
        {
            return;
        }

        var items = CreateItems();
        for (var index = 0; index < items.Count; index++)
        {
            await context.Set<CatalogItem>().AddAsync(items[index], cancellationToken);
            EnsureIdFromTheNewSequence(context, items, index);
        }

        await context.SaveChangesAsync(cancellationToken);
        StopTracking(context, items);
    }

    // The sample items of the legacy app (PreconfiguredData.cs, docs/legacy/seed-data.json), in the order
    // in which HiLo gives them IDs 1-12 from a new sequence, as the legacy seeding did. New instances
    // for every call, because the context tracks them.
    internal static IReadOnlyList<CatalogItem> CreateItems() =>
    [
        Item(".NET Bot Black Hoodie", 19.50m, "1.png", catalogTypeId: 2, catalogBrandId: 2),
        Item(".NET Black & White Mug", 8.50m, "2.png", catalogTypeId: 1, catalogBrandId: 2),
        Item("Prism White T-Shirt", 12.00m, "3.png", catalogTypeId: 2, catalogBrandId: 5),
        Item(".NET Foundation T-shirt", 12.00m, "4.png", catalogTypeId: 2, catalogBrandId: 2),
        Item("Roslyn Red Sheet", 8.50m, "5.png", catalogTypeId: 3, catalogBrandId: 5),
        Item(".NET Blue Hoodie", 12.00m, "6.png", catalogTypeId: 2, catalogBrandId: 2),
        Item("Roslyn Red T-Shirt", 12.00m, "7.png", catalogTypeId: 2, catalogBrandId: 5),
        Item("Kudu Purple Hoodie", 8.50m, "8.png", catalogTypeId: 2, catalogBrandId: 5),
        Item("Cup<T> White Mug", 12.00m, "9.png", catalogTypeId: 1, catalogBrandId: 5),
        Item(".NET Foundation Sheet", 12.00m, "10.png", catalogTypeId: 3, catalogBrandId: 2),
        Item("Cup<T> Sheet", 8.50m, "11.png", catalogTypeId: 3, catalogBrandId: 2),
        Item("Prism White TShirt", 12.00m, "12.png", catalogTypeId: 2, catalogBrandId: 5),
    ];

    // HiLo numbers the items as they are added, 1-12 from a new sequence. EF Core keeps its blocks in memory
    // for the life of the process, even when the sequence is dropped and created again, as a revert and a
    // new Migrate do. Items numbered from such a block would collide later with the new sequence's own
    // blocks. The check runs as each item is added, so it refuses an old block before anything is drawn
    // from the new sequence, and a new process can still seed the database.
    private static void EnsureIdFromTheNewSequence(DbContext context, IReadOnlyList<CatalogItem> items, int index)
    {
        if (items[index].Id == index + 1)
        {
            return;
        }

        StopTracking(context, items);
        throw new InvalidOperationException(
            $"The sample items did not get IDs 1-{items.Count} from the new '{CatalogDbContext.ItemIdSequence}' sequence, " +
            "because this process still holds IDs drawn before the sequence was created again. Migrate from a new process.");
    }

    // The seeder works on the caller's context, which may go on to other work, such as migrating again.
    private static void StopTracking(DbContext context, IEnumerable<CatalogItem> items)
    {
        foreach (var item in items)
        {
            context.Entry(item).State = EntityState.Detached;
        }
    }

    private static CatalogItem Item(string name, decimal price, string pictureFileName, int catalogTypeId, int catalogBrandId) => new()
    {
        Name = name,
        Description = name,
        Price = price,
        PictureFileName = pictureFileName,
        CatalogTypeId = catalogTypeId,
        CatalogBrandId = catalogBrandId,
        AvailableStock = 100,
    };

    // True when the item-ID sequence has never handed out a value, and the database has none of the
    // objects that only a legacy database has: EF6's history table and the two unused sequences, which
    // the baseline keeps in an adopted database (ADR-0012). The caller has already checked for items.
    private static FormattableString IsUnusedQuery() =>
        $"""
        SELECT CAST(CASE
            WHEN EXISTS (SELECT 1 FROM sys.sequences WHERE object_id = OBJECT_ID({CatalogDbContext.Schema + "." + CatalogDbContext.ItemIdSequence}) AND last_used_value IS NOT NULL) THEN 0
            WHEN OBJECT_ID(N'dbo.__MigrationHistory', N'U') IS NOT NULL THEN 0
            WHEN OBJECT_ID(N'dbo.catalog_brand_hilo', N'SO') IS NOT NULL THEN 0
            WHEN OBJECT_ID(N'dbo.catalog_type_hilo', N'SO') IS NOT NULL THEN 0
            ELSE 1
        END AS bit) AS [Value]
        """;
}
