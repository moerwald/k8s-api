using System.Text.Json;
using k8s;

namespace K8sApi.HttpRoutes;

public interface IGatewayApiReader
{
    Task<List<HttpRoute>> ListHttpRoutesAsync(string labelSelector, CancellationToken ct);
    Task<List<Gateway>> ListGatewaysAsync(CancellationToken ct);
}

/// <summary>Reads Gateway API objects from all namespaces.</summary>
public sealed class GatewayApiReader(IKubernetes client) : IGatewayApiReader
{
    private const string Group = "gateway.networking.k8s.io";
    private const string Version = "v1";
    private const int PageSize = 500;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public Task<List<HttpRoute>> ListHttpRoutesAsync(string labelSelector, CancellationToken ct) =>
        ListAllAsync<HttpRoute>("httproutes", labelSelector, ct);

    public Task<List<Gateway>> ListGatewaysAsync(CancellationToken ct) =>
        ListAllAsync<Gateway>("gateways", labelSelector: null, ct);

    // Reads page by page, so large clusters do not load everything in one call.
    private async Task<List<T>> ListAllAsync<T>(string plural, string? labelSelector, CancellationToken ct)
    {
        var all = new List<T>();
        string? next = null;

        do
        {
            var raw = await client.CustomObjects.ListClusterCustomObjectAsync(
                group: Group,
                version: Version,
                plural: plural,
                continueParameter: next,
                labelSelector: labelSelector,
                limit: PageSize,
                cancellationToken: ct);

            var page = Deserialize<ResourceList<T>>(raw);
            if (page is null) break;

            all.AddRange(page.Items);
            next = page.Metadata?.Continue;
        }
        while (!string.IsNullOrEmpty(next));

        return all;
    }

    // The client returns "object" (a JsonElement). Turn it into our model.
    private static TOut? Deserialize<TOut>(object raw) => raw switch
    {
        JsonElement el => el.Deserialize<TOut>(JsonOptions),
        _ => JsonSerializer.Deserialize<TOut>(JsonSerializer.Serialize(raw), JsonOptions),
    };
}
