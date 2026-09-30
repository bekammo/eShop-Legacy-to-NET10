namespace eShop.Catalog.Api.Catalog;

// The Catalog configuration section (ADR-0009, ADR-0017).
internal sealed class CatalogOptions
{
    internal const string SectionName = "Catalog";

    // Serves the catalog from memory, starting with the legacy sample data, and uses no database (the legacy
    // UseMockData). Changes are lost when the process ends.
    public bool UseMockData { get; set; }
}
