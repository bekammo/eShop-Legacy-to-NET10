using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace eShop.Catalog.Api.IntegrationTests;

internal static class AccessTokens
{
    private const string Issuer = "eShop.Catalog.Api.IntegrationTests";

    private const string Audience = "eShop.Catalog.Api";

    private static readonly SymmetricSecurityKey Key = new(RandomNumberGenerator.GetBytes(32));

    public static IEnumerable<KeyValuePair<string, string?>> Settings =>
    [
        new("Authentication:Schemes:Bearer:ValidIssuer", Issuer),
        new("Authentication:Schemes:Bearer:ValidAudiences:0", Audience),
        new("Authentication:Schemes:Bearer:SigningKeys:0:Issuer", Issuer),
        new("Authentication:Schemes:Bearer:SigningKeys:0:Value", Convert.ToBase64String(Key.Key)),
    ];

    public static AuthenticationHeaderValue Writer => Bearer(Create("catalog:write"));

    public static AuthenticationHeaderValue Bearer(string token) => new("Bearer", token);

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
