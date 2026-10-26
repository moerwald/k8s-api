using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

namespace K8sApi.Auth;

public static class Policies
{
    /// <summary>Keycloak client role for all read endpoints (spec A2).</summary>
    public const string Reader = "reader";
}

/// <summary>
/// Keycloak sends client roles like this (spec A1, A2):
///   "resource_access": { "k8s-api": { "roles": ["reader", ...] } }
/// ASP.NET does not understand this format. This class adds one role claim per role of OUR client only.
/// Realm roles ("realm_access") and roles of other clients are ignored (spec A3, A4).
/// </summary>
public sealed class KeycloakRoleClaims(string clientId, ILogger<KeycloakRoleClaims> log) : IClaimsTransformation
{
    /// <summary>
    /// Claim type for the mapped roles. It is our own name, so a claim in the token
    /// can't pretend to be a mapped role.
    /// </summary>
    public const string RoleClaimType = "k8s-api-role";

    // Set after the first run. TransformAsync can run more than once per request.
    private const string CheckedClaimType = "k8s-api-roles-checked";

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity { IsAuthenticated: true } identity)
            return Task.FromResult(principal);

        if (identity.HasClaim(c => c.Type == CheckedClaimType))
            return Task.FromResult(principal);

        identity.AddClaim(new Claim(CheckedClaimType, "true"));

        var resourceAccess = identity.FindFirst("resource_access")?.Value;
        if (string.IsNullOrEmpty(resourceAccess))
            return Task.FromResult(principal); // no client roles at all → 403 later (spec A9)

        var problem = AddClientRoles(identity, resourceAccess);
        if (problem is not null)
        {
            // Spec A10: broken role data → no roles (so 403) and a warning.
            log.LogWarning("Token of user {User} has broken role data in resource_access: {Problem}",
                identity.FindFirst("sub")?.Value ?? "(unknown)", problem);
        }

        return Task.FromResult(principal);
    }

    /// <summary>Adds the roles of our client. Returns a problem text when the data is broken, else null.</summary>
    private string? AddClientRoles(ClaimsIdentity identity, string resourceAccess)
    {
        try
        {
            using var doc = JsonDocument.Parse(resourceAccess);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return "resource_access is not an object";

            if (!doc.RootElement.TryGetProperty(clientId, out var client))
                return null; // roles only for other clients → 403 later (spec A4)

            if (client.ValueKind != JsonValueKind.Object ||
                !client.TryGetProperty("roles", out var roles) ||
                roles.ValueKind != JsonValueKind.Array)
                return $"resource_access.{clientId}.roles is not a list";

            var roleNames = new List<string>();
            foreach (var role in roles.EnumerateArray())
            {
                if (role.ValueKind != JsonValueKind.String)
                    return $"resource_access.{clientId}.roles has an entry that is not text";
                if (role.GetString() is { Length: > 0 } name)
                    roleNames.Add(name);
            }

            // Add roles only when all data is fine.
            foreach (var name in roleNames)
                identity.AddClaim(new Claim(RoleClaimType, name));
            return null;
        }
        catch (JsonException)
        {
            return "resource_access is not valid JSON";
        }
    }
}
