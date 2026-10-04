using System.ComponentModel.DataAnnotations;

namespace eShop.Catalog.Api.Items;

// A price has at most two decimal places, as the decimal(18,2) column holds it and the legacy form's regular expression
// required (ADR-0025). SQL Server would round a third one away.
/// <summary>Validates that a decimal has at most two decimal places.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class TwoDecimalPlacesAttribute() : ValidationAttribute("The field {0} must have at most two decimal places.")
{
    /// <inheritdoc />
    public override bool IsValid(object? value) => value is not decimal price || decimal.Round(price, 2) == price;
}
