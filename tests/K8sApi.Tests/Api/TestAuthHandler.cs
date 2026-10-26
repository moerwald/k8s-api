using System.Security.Claims;
using System.Text.Encodings.Web;
using K8sApi.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace K8sApi.Tests.Api;

/// <summary>
/// Test login: every request is a logged-in user with the reader role.
/// Used by tests that are not about auth (for example error tests).
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity(
            [
                new Claim("preferred_username", "test-user"),
                new Claim(KeycloakRoleClaims.RoleClaimType, Policies.Reader),
            ],
            SchemeName,
            "preferred_username",
            KeycloakRoleClaims.RoleClaimType);

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
