namespace eShop.Catalog.Api.Authorization;

// The scopes of the API's access tokens (ADR-0034). A scope is also the name of the policy that requires it.
internal static class CatalogScopes
{
    // Creating, updating and deleting items.
    internal const string Write = "catalog:write";
}
