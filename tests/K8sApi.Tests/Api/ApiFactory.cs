using K8sApi.HttpRoutes;
using K8sApi.Tests.HttpRoutes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace K8sApi.Tests.Api;

/// <summary>
/// Starts the real API in memory for endpoint tests:
/// - Environment "Production", so the app behaves like in a cluster (no developer error page).
/// - A fake Kubernetes cluster (<see cref="Cluster"/>) instead of a real one.
/// - A test login (<see cref="TestAuthHandler"/>) instead of Keycloak.
/// - All log lines are kept in <see cref="Logs"/>.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
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
}
