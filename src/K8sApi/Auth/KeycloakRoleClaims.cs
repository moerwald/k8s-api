using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

namespace K8sApi.Auth;

public static class Policies
{
    /// <summary>Keycloak realm role for all read endpoints.</summary>
    public const string Reader = "k8s-api-reader";
}

/// <summary>
/// Keycloak sends realm roles like this:
///   "realm_access": { "roles": ["k8s-api-reader", ...] }
/// ASP.NET does not understand this format. This class adds one "role" claim per role.
/// </summary>
public sealed class KeycloakRoleClaims : IClaimsTransformation
{
    public const string RoleClaimType = "role";

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity { IsAuthenticated: true } identity)
            return Task.FromResult(principal);

        // TransformAsync can run more than once per request. Do the work only once.
        if (identity.HasClaim(c => c.Type == RoleClaimType))
            return Task.FromResult(principal);

        var realmAccess = identity.FindFirst("realm_access")?.Value;
        if (string.IsNullOrEmpty(realmAccess))
            return Task.FromResult(principal);

        try
        {
            using var doc = JsonDocument.Parse(realmAccess);
            if (doc.RootElement.TryGetProperty("roles", out var roles) &&
                roles.ValueKind == JsonValueKind.Array)
            {
                foreach (var role in roles.EnumerateArray())
                {
                    var name = role.GetString();
                    if (!string.IsNullOrEmpty(name))
                        identity.AddClaim(new Claim(RoleClaimType, name));
                }
            }
        }
        catch (JsonException)
        {
            // Bad claim format: add no roles. The user will get 403.
        }

        return Task.FromResult(principal);
    }
}
