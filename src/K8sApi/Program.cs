using k8s;
using K8sApi.Auth;
using K8sApi.HttpRoutes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);

// ---------- Kubernetes client ----------
// The API connects with a kubeconfig file. Its path is required.
// The current-context of the file is used. Done here, so a bad config stops the app at startup.
var kubeconfigPath = builder.Configuration["Kubernetes:KubeconfigPath"];
if (string.IsNullOrWhiteSpace(kubeconfigPath))
    throw new InvalidOperationException(
        "Setting 'Kubernetes:KubeconfigPath' is missing. Set it in appsettings.json or as env variable Kubernetes__KubeconfigPath.");
if (!File.Exists(kubeconfigPath))
    throw new FileNotFoundException($"Kubeconfig file not found: '{kubeconfigPath}'.", kubeconfigPath);

var kubeConfig = KubernetesClientConfiguration.BuildConfigFromConfigFile(kubeconfigPath);
builder.Services.AddSingleton<IKubernetes>(new Kubernetes(kubeConfig));

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
