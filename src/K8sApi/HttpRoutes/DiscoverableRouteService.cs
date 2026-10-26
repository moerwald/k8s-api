using K8sApi.Auth;

namespace K8sApi.HttpRoutes;

public sealed class DiscoverableRouteService(IGatewayApiReader reader, ILogger<DiscoverableRouteService> log)
{
    // Only ask Kubernetes for routes that HAVE both labels.
    // The value check ("true" in any case, domain not empty) is done in UrlBuilder,
    // because Kubernetes label selectors are case-sensitive.
    public const string LabelSelector = "discoverable,domain";

    public async Task<DiscoverableRoutesResponse> GetAsync(CancellationToken ct)
    {
        // Both calls are independent, so run them at the same time.
        var routesTask = reader.ListHttpRoutesAsync(LabelSelector, ct);
        var gatewaysTask = reader.ListGatewaysAsync(ct);
        await Task.WhenAll(routesTask, gatewaysTask);

        var result = UrlBuilder.Build(routesTask.Result, gatewaysTask.Result);

        // Spec L1: routes without URL are hidden in the answer, so log them for the operator.
        foreach (var r in result.RoutesWithoutUrl)
        {
            log.LogWarning("HTTPRoute {Namespace}/{Name} (domain {Domain}) gives no URL: {Reason}",
                r.Namespace, r.Name, r.Domain, r.Reason);
        }

        return result.Response;
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
