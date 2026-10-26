using K8sApi.HttpRoutes;

namespace K8sApi.Tests.HttpRoutes;

/// <summary>
/// Fake Kubernetes cluster. It holds routes and gateways in memory.
/// It applies the label selector like the real Kubernetes API does
/// (case-sensitive, terms separated by commas), so tests see what a real cluster would return.
/// </summary>
public sealed class FakeGatewayApiReader : IGatewayApiReader
{
    public List<HttpRoute> Routes { get; } = new();
    public List<Gateway> Gateways { get; } = new();

    public Task<List<HttpRoute>> ListHttpRoutesAsync(string labelSelector, CancellationToken ct) =>
        Task.FromResult(Routes.Where(r => Matches(labelSelector, r.Metadata.Labels)).ToList());

    public Task<List<Gateway>> ListGatewaysAsync(CancellationToken ct) =>
        Task.FromResult(Gateways.ToList());

    // Supports the equality-based selector forms: "k=v", "k==v", "k!=v", "k", "!k".
    private static bool Matches(string? selector, Dictionary<string, string>? labels)
    {
        if (string.IsNullOrWhiteSpace(selector)) return true;
        labels ??= new();

        foreach (var raw in selector.Split(','))
        {
            var term = raw.Trim();
            if (term.Length == 0) continue;

            bool ok;
            if (term.Contains("!="))
            {
                var (k, v) = Split(term, "!=");
                ok = !labels.TryGetValue(k, out var actual) || actual != v;
            }
            else if (term.Contains("=="))
            {
                var (k, v) = Split(term, "==");
                ok = labels.TryGetValue(k, out var actual) && actual == v;
            }
            else if (term.Contains('='))
            {
                var (k, v) = Split(term, "=");
                ok = labels.TryGetValue(k, out var actual) && actual == v;
            }
            else if (term.StartsWith('!'))
            {
                ok = !labels.ContainsKey(term[1..].Trim());
            }
            else
            {
                ok = labels.ContainsKey(term);
            }

            if (!ok) return false;
        }
        return true;
    }

    private static (string Key, string Value) Split(string term, string op)
    {
        var i = term.IndexOf(op, StringComparison.Ordinal);
        return (term[..i].Trim(), term[(i + op.Length)..].Trim());
    }
}
