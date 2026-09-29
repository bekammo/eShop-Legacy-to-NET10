namespace eShop.Catalog.Api.Data;

// The Database configuration section (ADR-0009, ADR-0013).
internal sealed class DatabaseOptions
{
    internal const string SectionName = "Database";

    // Applies the pending migrations, and with them the sample items, before the host accepts requests.
    // Development only: deployed environments apply the reviewed idempotent script (ADR-0011), and a
    // host in any other environment refuses to start with this on.
    public bool MigrateOnStartup { get; set; }
}
