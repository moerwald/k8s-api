using k8s;
using K8sApi.Auth;
using K8sApi.HttpRoutes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);

// ---------- Kubernetes client ----------
// In the cluster: use the service account. Local: use ~/.kube/config.
builder.Services.AddSingleton<IKubernetes>(_ =>
{
    var config = KubernetesClientConfiguration.IsInCluster()
        ? KubernetesClientConfiguration.InClusterConfig()
        : KubernetesClientConfiguration.BuildConfigFromConfigFile();
    return new Kubernetes(config);
});

builder.Services.AddSingleton<IGatewayApiReader, GatewayApiReader>();
builder.Services.AddSingleton<DiscoverableRouteService>();

// ---------- Keycloak authentication ----------
var keycloak = builder.Configuration.GetSection("Keycloak");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Example: https://keycloak.example.com/realms/my-realm
        options.Authority = keycloak["Authority"];
        options.Audience = keycloak["Audience"];
        options.RequireHttpsMetadata = keycloak.GetValue("RequireHttpsMetadata", true);
        options.MapInboundClaims = false;
        options.TokenValidationParameters.NameClaimType = "preferred_username";
        options.TokenValidationParameters.RoleClaimType = KeycloakRoleClaims.RoleClaimType;
    });

// Keycloak puts realm roles in "realm_access.roles". This copies them into normal role claims.
builder.Services.AddTransient<IClaimsTransformation, KeycloakRoleClaims>();

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.Reader, p => p.RequireRole(Policies.Reader));

builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/healthz", () => Results.Ok("ok")).AllowAnonymous();

app.MapDiscoverableRoutes();

app.Run();
