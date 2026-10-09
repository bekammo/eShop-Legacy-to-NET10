using System.ComponentModel.DataAnnotations;

namespace eShop.Catalog.Api.Catalog;

internal sealed class CatalogOptions
{
    internal const string SectionName = "Catalog";

    public bool UseMockData { get; set; }

    [Required]
    public string? PicturesPath { get; set; }
}
