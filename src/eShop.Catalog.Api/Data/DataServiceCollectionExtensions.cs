using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

namespace eShop.Catalog.Api.Data;

internal static class DataServiceCollectionExtensions
{
    internal const string ConnectionStringName = "CatalogDb";

    // Registers CatalogDbContext against ConnectionStrings:CatalogDb. Without a connection string
    // the host stops at startup instead of failing on the first query (ADR-0009, ADR-0010).
    internal static IServiceCollection AddCatalogDbContext(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"The connection string 'ConnectionStrings:{ConnectionStringName}' is not set. Development reads it " +
                "from appsettings.Development.json. Other environments must supply it, for example as the " +
                $"environment variable ConnectionStrings__{ConnectionStringName}.");
        }

        return services.AddDbContext<CatalogDbContext>(options => options.UseCatalogSqlServer(connectionString));
    }

    // Database:MigrateOnStartup, allowed only in Development (ADR-0013). Any other environment stops at
    // startup with it on, so that a stray environment variable cannot migrate a deployed database.
    internal static IServiceCollection AddMigrateOnStartup(this IServiceCollection services)
    {
        services.AddOptions<DatabaseOptions>()
            .BindConfiguration(DatabaseOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate<IHostEnvironment>(
                static (options, environment) => !options.MigrateOnStartup || environment.IsDevelopment(),
                $"{DatabaseOptions.SectionName}:{nameof(DatabaseOptions.MigrateOnStartup)} is allowed only in the " +
                "Development environment. Other environments apply the migrations with the idempotent script.")
            .ValidateOnStart();

        return services.AddHostedService<MigrateOnStartupService>();
    }

    // The options of every CatalogDbContext, so that the app, the tests and the dotnet-ef tool build the
    // same one:
    // - The migrations history table is not in the model, so HasDefaultSchema does not reach it.
    // - Every Migrate and dotnet ef database update ends with the sample-item seeder (ADR-0013).
    internal static DbContextOptionsBuilder UseCatalogSqlServer(this DbContextOptionsBuilder options, string? connectionString = null) =>
        options.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, CatalogDbContext.Schema))
            .UseSeeding(SampleItemSeeder.Seed)
            .UseAsyncSeeding(SampleItemSeeder.SeedAsync);
}
