using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace K8sApi.Tests.Api;

/// <summary>
/// Makes signed JWTs that look like Keycloak access tokens.
/// The API trusts <see cref="SigningKey"/> and <see cref="Issuer"/> in tests (see <see cref="ApiFactory"/>).
/// </summary>
public static class TestTokens
{
    public const string Issuer = "https://keycloak.test/realms/test";
    public const string Audience = "k8s-api"; // spec A1, same as Keycloak:Audience in appsettings.json

    /// <summary>The key the API trusts (like Keycloak's public key).</summary>
    public static readonly RsaSecurityKey SigningKey = NewKey("trusted");

    /// <summary>A key the API does NOT trust.</summary>
    public static readonly RsaSecurityKey OtherKey = NewKey("other");

    private static RsaSecurityKey NewKey(string id) => new(RSA.Create(2048)) { KeyId = id };

    /// <summary>
    /// Builds a token. Defaults: trusted key, right issuer and audience, valid for 5 minutes, no roles.
    /// resourceAccess / realmAccess are put into the token as they are (object → JSON, string → string).
    /// </summary>
    public static string Create(
        object? resourceAccess = null,
        object? realmAccess = null,
        string issuer = Issuer,
        string audience = Audience,
        SecurityKey? key = null,
        DateTime? expires = null)
    {
        var claims = new Dictionary<string, object>
        {
            ["preferred_username"] = "test-user",
            ["sub"] = "test-user-id",
        };
        if (resourceAccess is not null) claims["resource_access"] = resourceAccess;
        if (realmAccess is not null) claims["realm_access"] = realmAccess;

        var exp = expires ?? DateTime.UtcNow.AddMinutes(5);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = claims,
            IssuedAt = exp.AddMinutes(-10),
            NotBefore = exp.AddMinutes(-10),
            Expires = exp,
            SigningCredentials = new SigningCredentials(key ?? SigningKey, SecurityAlgorithms.RsaSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    /// <summary>"resource_access" with roles for one client, like Keycloak writes it.</summary>
    public static Dictionary<string, object> ClientRoles(string client, params string[] roles) =>
        new() { [client] = new Dictionary<string, object> { ["roles"] = roles } };

    /// <summary>"realm_access" with realm roles, like Keycloak writes it.</summary>
    public static Dictionary<string, object> RealmRoles(params string[] roles) =>
        new() { ["roles"] = roles };
}
