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
    /// <summary>Spec E5: the API waits at most this long for Kubernetes.</summary>
    public static readonly TimeSpan KubernetesTimeout = TimeSpan.FromSeconds(10);

    public static void MapDiscoverableRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/httproutes/discoverable", async (
                DiscoverableRouteService service,
                ILogger<DiscoverableRouteService> log,
                CancellationToken requestAborted) =>
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
                timeout.CancelAfter(KubernetesTimeout);

                try
                {
                    return Results.Ok(await service.GetAsync(timeout.Token));
                }
                catch (OperationCanceledException) when (requestAborted.IsCancellationRequested)
                {
                    // Spec E8: the client went away. Not an error.
                    log.LogInformation("Request cancelled by client");
                    return Results.Empty;
                }
                catch (OperationCanceledException ex) when (timeout.IsCancellationRequested)
                {
                    log.LogError(ex, "Kubernetes API did not answer within {Seconds} seconds",
                        KubernetesTimeout.TotalSeconds);
                    return Problem(StatusCodes.Status504GatewayTimeout, "Kubernetes API timeout");
                }
                catch (k8s.Autorest.HttpOperationException ex)
                {
                    var status = ex.Response?.StatusCode;
                    log.LogError(ex, "Kubernetes API answered with status {StatusCode}", status);
                    return Problem(StatusCodes.Status502BadGateway, status switch
                    {
                        System.Net.HttpStatusCode.Forbidden => "Kubernetes API access forbidden",
                        System.Net.HttpStatusCode.Unauthorized => "Kubernetes API authentication failed",
                        // Listing HTTPRoutes or Gateways gives 404 when the Gateway API CRDs are missing.
                        System.Net.HttpStatusCode.NotFound => "Gateway API not installed",
                        // Other Kubernetes errors (for example 500, 503) are not in the spec yet.
                        _ => "Kubernetes API not reachable",
                    });
                }
                catch (HttpRequestException ex)
                {
                    log.LogError(ex, "Kubernetes API not reachable");
                    return Problem(StatusCodes.Status502BadGateway, "Kubernetes API not reachable");
                }
                catch (System.Text.Json.JsonException ex)
                {
                    log.LogError(ex, "Kubernetes API sent data that could not be read");
                    return Problem(StatusCodes.Status502BadGateway, "Kubernetes API sent invalid data");
                }
                // Everything else (spec E7) is handled by the global exception handler in Program.cs.
            })
            .RequireAuthorization(Policies.Reader)
            .WithName("GetDiscoverableHttpRoutes");
    }

    // Spec E9/E10: only title and status. traceId is added by the problem details service.
    private static IResult Problem(int status, string title) =>
        Results.Problem(title: title, statusCode: status);
}
