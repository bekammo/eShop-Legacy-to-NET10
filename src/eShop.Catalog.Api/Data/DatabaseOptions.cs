namespace eShop.Catalog.Api.Data;

internal sealed class DatabaseOptions
{
    internal const string SectionName = "Database";

    public bool MigrateOnStartup { get; set; }
}
