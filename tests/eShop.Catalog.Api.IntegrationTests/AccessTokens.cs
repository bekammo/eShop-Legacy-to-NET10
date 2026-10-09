using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace eShop.Catalog.Api.IntegrationTests;

// Access tokens for the test hosts (ADR-0034), made as dotnet user-jwts makes them in Development: signed with a
// symmetric key that the host's Authentication:Schemes:Bearer section holds, beside the issuer and the audience that it
// accepts. CatalogApiFactory gives every host that section, with a key of the test run's own.
internal static class AccessTokens
{
    private const string Issuer = "eShop.Catalog.Api.IntegrationTests";

    private const string Audience = "eShop.Catalog.Api";

    private static readonly SymmetricSecurityKey Key = new(RandomNumberGenerator.GetBytes(32));

    // The section, in the form in which user-jwts writes it to appsettings.Development.json and to user secrets.
    public static IEnumerable<KeyValuePair<string, string?>> Settings =>
    [
        new("Authentication:Schemes:Bearer:ValidIssuer", Issuer),
        new("Authentication:Schemes:Bearer:ValidAudiences:0", Audience),
        new("Authentication:Schemes:Bearer:SigningKeys:0:Issuer", Issuer),
        new("Authentication:Schemes:Bearer:SigningKeys:0:Value", Convert.ToBase64String(Key.Key)),
    ];

    // A valid token with the catalog:write scope, for the item writes.
    public static AuthenticationHeaderValue Writer => Bearer(Create("catalog:write"));

    public static AuthenticationHeaderValue Bearer(string token) => new("Bearer", token);

    // A token with the scope claim given, a string or an array of strings. It is valid for an hour, unless the test sets
    // when it expired, and signed with the test run's key, unless the test gives another.
    public static string Create(
        object scope, string issuer = Issuer, string audience = Audience, SecurityKey? key = null, DateTime? expired = null)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = new Dictionary<string, object> { ["sub"] = "integration-tests", ["scope"] = scope },
            SigningCredentials = new SigningCredentials(key ?? Key, SecurityAlgorithms.HmacSha256),
        };
        if (expired is { } expires)
        {
            descriptor.NotBefore = expires.AddHours(-1);
            descriptor.Expires = expires;
        }

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
