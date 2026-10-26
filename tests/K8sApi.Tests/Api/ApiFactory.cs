using K8sApi.HttpRoutes;
using K8sApi.Tests.HttpRoutes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace K8sApi.Tests.Api;

/// <summary>
/// Starts the real API in memory for endpoint tests:
/// - Environment "Production", so the app behaves like in a cluster (no developer error page).
/// - A fake Kubernetes cluster (<see cref="Cluster"/>) instead of a real one.
/// - Login: by default a test login (<see cref="TestAuthHandler"/>, always the reader role).
///   With <c>realJwtAuth: true</c> the API's real JWT check runs, but trusts <see cref="TestTokens"/>
///   instead of Keycloak (same audience, role mapping and policies as in production).
/// - All log lines are kept in <see cref="Logs"/>.
/// </summary>
public sealed class ApiFactory(bool realJwtAuth = false) : WebApplicationFactory<Program>
{
    // One kubeconfig file for all tests. It is never used to connect, because the fake
    // cluster replaces the Kubernetes reader. The API only checks at startup that it exists and can be read.
    // It is also set as env variable, because Program.cs reads it before the app is built.
    private static readonly string KubeconfigPath = CreateKubeconfig();

    public FakeGatewayApiReader Cluster { get; } = new();
    public CapturingLoggerProvider Logs { get; } = new();

    private static string CreateKubeconfig()
    {
        var path = Path.Combine(Path.GetTempPath(), $"k8s-api-test-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, """
            apiVersion: v1
            kind: Config
            clusters:
            - name: test
              cluster:
                server: https://127.0.0.1:6443
            users:
            - name: test
              user:
                token: test-token
            contexts:
            - name: test
              context:
                cluster: test
                user: test
            current-context: test
            """);
        Environment.SetEnvironmentVariable("Kubernetes__KubeconfigPath", path);
        return path;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("Kubernetes:KubeconfigPath", KubeconfigPath);

        builder.ConfigureLogging(logging => logging.AddProvider(Logs));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IGatewayApiReader>();
            services.AddSingleton<IGatewayApiReader>(Cluster);

            if (realJwtAuth)
            {
                // Keep the real JWT check. Only replace Keycloak's address and key with the test ones,
                // so no network call to Keycloak is made.
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
                {
                    var config = new OpenIdConnectConfiguration { Issuer = TestTokens.Issuer };
                    config.SigningKeys.Add(TestTokens.SigningKey);
                    o.ConfigurationManager = new FixedConfiguration(config);
                    o.TokenValidationParameters.ValidIssuer = TestTokens.Issuer;
                    o.TokenValidationParameters.IssuerSigningKey = TestTokens.SigningKey;
                });
                return;
            }

            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            services.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultScheme = TestAuthHandler.SchemeName;
                o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            });
        });
    }

    /// <summary>Gives the JWT check a fixed issuer and key instead of loading them from Keycloak.</summary>
    private sealed class FixedConfiguration(OpenIdConnectConfiguration config)
        : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel) => Task.FromResult(config);
        public void RequestRefresh() { }
    }
}
