using System.Net.Http.Headers;
using System.Text.Json;
using K8sApi.Tests.Api;
using Microsoft.Extensions.Logging;
using static K8sApi.Tests.Api.TestTokens;

namespace K8sApi.Tests.Auth;

/// <summary>
/// Spec rules A1–A12: Keycloak login and roles. See docs/features/02-keycloak-auth.md.
/// The real API runs in memory with its real JWT check. Tokens are signed with a test key.
/// </summary>
public class AuthRuleTests
{
    private const string ReadUrl = "/api/v1/httproutes/discoverable";

    private sealed record Call(ApiFactory Api, HttpResponseMessage Response, string Body);

    private static async Task<Call> Get(string url, string? token)
    {
        var api = new ApiFactory(realJwtAuth: true);
        var client = api.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request);
        return new Call(api, response, await response.Content.ReadAsStringAsync());
    }

    // ---------- A1, A2: client role "reader" on client "k8s-api" ----------

    [Fact]
    public async Task A2_client_role_reader_on_k8s_api_is_allowed()
    {
        var call = await Get(ReadUrl, Create(resourceAccess: ClientRoles("k8s-api", "reader")));
        using var _ = call.Api;

        Assert.Equal(200, (int)call.Response.StatusCode);
    }

    [Fact]
    public async Task A2_reader_together_with_other_roles_and_clients_is_allowed()
    {
        var resourceAccess = new Dictionary<string, object>
        {
            ["account"] = new Dictionary<string, object> { ["roles"] = new[] { "view-profile" } },
            ["k8s-api"] = new Dictionary<string, object> { ["roles"] = new[] { "something", "reader" } },
        };

        var call = await Get(ReadUrl, Create(resourceAccess: resourceAccess));
        using var _ = call.Api;

        Assert.Equal(200, (int)call.Response.StatusCode);
    }

    // ---------- A3, A4: other places of the role are not accepted ----------

    [Fact]
    public async Task A3_old_realm_role_is_not_accepted()
    {
        var call = await Get(ReadUrl, Create(realmAccess: RealmRoles("k8s-api-reader")));
        using var _ = call.Api;

        Assert.Equal(403, (int)call.Response.StatusCode);
    }

    [Fact]
    public async Task A3_realm_role_named_reader_is_not_accepted()
    {
        var call = await Get(ReadUrl, Create(realmAccess: RealmRoles("reader")));
        using var _ = call.Api;

        Assert.Equal(403, (int)call.Response.StatusCode);
    }

    [Fact]
    public async Task A4_reader_role_on_other_client_is_not_accepted()
    {
        var call = await Get(ReadUrl, Create(resourceAccess: ClientRoles("other-app", "reader")));
        using var _ = call.Api;

        Assert.Equal(403, (int)call.Response.StatusCode);
    }

    // ---------- A6–A8: 401 ----------

    [Fact]
    public async Task A6_no_token_gives_401()
    {
        var call = await Get(ReadUrl, token: null);
        using var _ = call.Api;

        Assert.Equal(401, (int)call.Response.StatusCode);
    }

    public static TheoryData<string> BadTokens => new() { "wrong signature", "wrong issuer", "wrong audience", "not a jwt" };

    private static string BadToken(string kind)
    {
        var roles = ClientRoles("k8s-api", "reader");
        return kind switch
        {
            "wrong signature" => Create(resourceAccess: roles, key: OtherKey),
            "wrong issuer" => Create(resourceAccess: roles, issuer: "https://evil.test/realms/test"),
            "wrong audience" => Create(resourceAccess: roles, audience: "other-app"),
            "not a jwt" => "this-is-not-a-token",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    [Theory]
    [MemberData(nameof(BadTokens))]
    public async Task A7_bad_token_gives_401(string kind)
    {
        var call = await Get(ReadUrl, BadToken(kind));
        using var _ = call.Api;

        Assert.Equal(401, (int)call.Response.StatusCode);
    }

    [Fact]
    public async Task A8_expired_token_gives_401()
    {
        // Expired 10 minutes ago (more than the usual 5 minutes clock tolerance).
        var token = Create(resourceAccess: ClientRoles("k8s-api", "reader"), expires: DateTime.UtcNow.AddMinutes(-10));

        var call = await Get(ReadUrl, token);
        using var _ = call.Api;

        Assert.Equal(401, (int)call.Response.StatusCode);
    }

    // ---------- A9, A10: 403 ----------

    [Fact]
    public async Task A9_valid_token_without_roles_gives_403()
    {
        var call = await Get(ReadUrl, Create());
        using var _ = call.Api;

        Assert.Equal(403, (int)call.Response.StatusCode);
    }

    [Fact]
    public async Task A9_other_client_role_on_k8s_api_gives_403()
    {
        var call = await Get(ReadUrl, Create(resourceAccess: ClientRoles("k8s-api", "viewer")));
        using var _ = call.Api;

        Assert.Equal(403, (int)call.Response.StatusCode);
    }

    public static TheoryData<string> BrokenRoleData => new() { "resource_access is text", "roles is not a list" };

    private static object BrokenResourceAccess(string kind) => kind switch
    {
        "resource_access is text" => "not-json{",
        "roles is not a list" => new Dictionary<string, object>
        {
            ["k8s-api"] = new Dictionary<string, object> { ["roles"] = "reader" },
        },
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    [Theory]
    [MemberData(nameof(BrokenRoleData))]
    public async Task A10_broken_role_data_gives_403_and_warning(string kind)
    {
        var call = await Get(ReadUrl, Create(resourceAccess: BrokenResourceAccess(kind)));
        using var _ = call.Api;

        Assert.Equal(403, (int)call.Response.StatusCode);
        Assert.True(
            call.Api.Logs.Entries.Any(e => e.Level == LogLevel.Warning && e.Category.StartsWith("K8sApi", StringComparison.Ordinal)),
            "No Warning was logged. Logs:\n" +
            string.Join("\n", call.Api.Logs.Entries.Select(e => $"- {e.Level} [{e.Category}] {e.Message}")));
    }

    // ---------- A11: health check ----------

    [Fact]
    public async Task A11_healthz_needs_no_token()
    {
        var call = await Get("/healthz", token: null);
        using var _ = call.Api;

        Assert.Equal(200, (int)call.Response.StatusCode);
    }

    // ---------- A12: problem body ----------

    [Fact]
    public async Task A12_401_body_is_problem_json_titled_unauthorized()
    {
        var call = await Get(ReadUrl, token: null);
        using var _ = call.Api;

        AssertProblem(call, 401, "Unauthorized");
    }

    [Fact]
    public async Task A12_403_body_is_problem_json_titled_forbidden()
    {
        var call = await Get(ReadUrl, Create());
        using var _ = call.Api;

        AssertProblem(call, 403, "Forbidden");
    }

    private static void AssertProblem(Call call, int status, string title)
    {
        Assert.Equal(status, (int)call.Response.StatusCode);
        Assert.Equal("application/problem+json", call.Response.Content.Headers.ContentType?.MediaType);
        Assert.False(string.IsNullOrWhiteSpace(call.Body), "The answer has no body.");

        using var doc = JsonDocument.Parse(call.Body);
        var problem = doc.RootElement;
        Assert.Equal(title, problem.GetProperty("title").GetString());
        Assert.Equal(status, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));

        var names = problem.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "status", "title", "traceId" }, names);
    }
}
