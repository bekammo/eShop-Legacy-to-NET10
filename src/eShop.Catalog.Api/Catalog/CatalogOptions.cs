using System.ComponentModel.DataAnnotations;

namespace eShop.Catalog.Api.Catalog;

// The Catalog configuration section (ADR-0009, ADR-0017, ADR-0023).
internal sealed class CatalogOptions
{
    internal const string SectionName = "Catalog";

    // Serves the catalog from memory, starting with the legacy sample data, and uses no database (the legacy
    // UseMockData). Changes are lost when the process ends.
    public bool UseMockData { get; set; }

    // The folder of the item pictures. A relative path is resolved against the content root, as the log file's is
    // (ADR-0018). appsettings.json names the project's Pics folder, which the publish copies with the app. The host does
    // not start when the folder does not exist (ADR-0023).
    [Required]
    public string? PicturesPath { get; set; }
}
