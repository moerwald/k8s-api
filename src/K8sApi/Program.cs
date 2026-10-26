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

// ---------- Error answers ----------
// All errors are "problem details" JSON with only title, status and traceId (spec E9, E10).
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    var problem = context.ProblemDetails;
    problem.Type = null;     // ASP.NET adds a link to the HTTP standard. Not wanted.
    problem.Detail = null;   // Never send internal details to the client.
    problem.Instance = null;

    // Spec E7: unexpected exceptions get a fixed title. The exception itself is only logged.
    if (context.Exception is not null)
        problem.Title = "Internal server error";
});

var app = builder.Build();

// Spec E7: unexpected exceptions → 500 problem details, logged as Error with the exception.
app.UseExceptionHandler();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/healthz", () => Results.Ok("ok")).AllowAnonymous();

app.MapDiscoverableRoutes();

app.Run();
