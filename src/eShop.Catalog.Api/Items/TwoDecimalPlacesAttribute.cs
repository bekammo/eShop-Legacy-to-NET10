using System.ComponentModel.DataAnnotations;

namespace eShop.Catalog.Api.Items;

/// <summary>Validates that a decimal has at most two decimal places.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class TwoDecimalPlacesAttribute() : ValidationAttribute("The field {0} must have at most two decimal places.")
{
    /// <inheritdoc />
    public override bool IsValid(object? value) => value is not decimal price || decimal.Round(price, 2) == price;
}
