using eShop.Catalog.Api.Catalog;
using Microsoft.EntityFrameworkCore;

namespace eShop.Catalog.Api.Data;

internal static class SampleItemSeeder
{
    // Needed although the app migrates asynchronously: dotnet ef database update migrates synchronously, and a
    // synchronous Migrate with only an async seeder throws.
    public static void Seed(DbContext context, bool storeManagementPerformed)
    {
        if (context.Database.GetPendingMigrations().Any()
            || context.Set<CatalogItem>().Any()
            || !context.Database.SqlQuery<bool>(IsUnusedQuery()).Single())
        {
            return;
        }

        var items = PreconfiguredData.CatalogItems();
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

        var items = PreconfiguredData.CatalogItems();
        for (var index = 0; index < items.Count; index++)
        {
            await context.Set<CatalogItem>().AddAsync(items[index], cancellationToken);
            EnsureIdFromTheNewSequence(context, items, index);
        }

        await context.SaveChangesAsync(cancellationToken);
        StopTracking(context, items);
    }

    // EF Core keeps HiLo blocks in memory after the sequence is dropped and created again (a revert, then Migrate).
    // Check each item as it is added, not after the loop, so that an old block is refused before anything is drawn
    // from the new sequence; otherwise the sequence counts as used and no new process can seed the database.
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

    private static void StopTracking(DbContext context, IEnumerable<CatalogItem> items)
    {
        foreach (var item in items)
        {
            context.Entry(item).State = EntityState.Detached;
        }
    }

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
