using K8sApi.Auth;

namespace K8sApi.HttpRoutes;

public sealed class DiscoverableRouteService(IGatewayApiReader reader)
{
    // discoverable must be "true", and the key "domain" must exist (any value).
    public const string LabelSelector = "discoverable=true,domain";

    public async Task<DiscoverableRoutesResponse> GetAsync(CancellationToken ct)
    {
        // Both calls are independent, so run them at the same time.
        var routesTask = reader.ListHttpRoutesAsync(LabelSelector, ct);
        var gatewaysTask = reader.ListGatewaysAsync(ct);
        await Task.WhenAll(routesTask, gatewaysTask);

        return UrlBuilder.Build(routesTask.Result, gatewaysTask.Result);
    }
}

public static class DiscoverableRouteEndpoints
{
    public static void MapDiscoverableRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/httproutes/discoverable", async (
                DiscoverableRouteService service,
                ILogger<DiscoverableRouteService> log,
                CancellationToken ct) =>
            {
                try
                {
                    return Results.Ok(await service.GetAsync(ct));
                }
                catch (Exception ex) when (ex is k8s.Autorest.HttpOperationException or HttpRequestException)
                {
                    log.LogError(ex, "Could not read Gateway API objects from Kubernetes");
                    return Results.Problem(
                        title: "Kubernetes API not available",
                        statusCode: StatusCodes.Status502BadGateway);
                }
            })
            .RequireAuthorization(Policies.Reader)
            .WithName("GetDiscoverableHttpRoutes");
    }
}
