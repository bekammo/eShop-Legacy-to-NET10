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

    // The SQL Server options of every CatalogDbContext, so that the app and the tests build the same
    // one. The migrations history table is not in the model, so HasDefaultSchema does not reach it.
    internal static DbContextOptionsBuilder UseCatalogSqlServer(this DbContextOptionsBuilder options, string? connectionString = null) =>
        options.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, CatalogDbContext.Schema));
}
