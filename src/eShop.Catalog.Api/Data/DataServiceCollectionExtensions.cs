using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

namespace eShop.Catalog.Api.Data;

internal static class DataServiceCollectionExtensions
{
    internal const string ConnectionStringName = "CatalogDb";

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

    // The schema argument is not redundant: HasDefaultSchema does not reach the migrations history table, which is
    // not in the model.
    internal static DbContextOptionsBuilder UseCatalogSqlServer(this DbContextOptionsBuilder options, string? connectionString = null) =>
        options.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, CatalogDbContext.Schema))
            .UseSeeding(SampleItemSeeder.Seed)
            .UseAsyncSeeding(SampleItemSeeder.SeedAsync);
}
