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

// Spec A1: the Keycloak client "k8s-api" is the audience and also holds the client roles.
var keycloakClientId = keycloak["Audience"];
if (string.IsNullOrWhiteSpace(keycloakClientId))
    throw new InvalidOperationException(
        "Setting 'Keycloak:Audience' is missing. Set it in appsettings.json or as env variable Keycloak__Audience.");

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

        // Spec A12: 401 and 403 answers are problem JSON like all other errors.
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse(); // skip the default empty 401
                context.Response.Headers.WWWAuthenticate = "Bearer";
                await WriteProblem(context.HttpContext, StatusCodes.Status401Unauthorized, "Unauthorized");
            },
            OnForbidden = context =>
                WriteProblem(context.HttpContext, StatusCodes.Status403Forbidden, "Forbidden"),
        };
    });

// Keycloak puts client roles in "resource_access.<client>.roles". This copies the roles of our client
// into normal role claims (spec A2–A4, A10).
builder.Services.AddTransient<IClaimsTransformation>(sp =>
    new KeycloakRoleClaims(keycloakClientId, sp.GetRequiredService<ILogger<KeycloakRoleClaims>>()));

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

static async Task WriteProblem(HttpContext http, int status, string title)
{
    http.Response.StatusCode = status;
    var problems = http.RequestServices.GetRequiredService<IProblemDetailsService>();
    await problems.WriteAsync(new ProblemDetailsContext
    {
        HttpContext = http,
        ProblemDetails = { Status = status, Title = title },
    });
}
