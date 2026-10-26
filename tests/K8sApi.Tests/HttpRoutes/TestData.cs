using K8sApi.HttpRoutes;

namespace K8sApi.Tests.HttpRoutes;

/// <summary>
/// Small helpers to build Gateway API objects for tests.
/// Defaults: Gateway "gw" in namespace "infra", route in namespace "shop", accepted by the Gateway.
/// </summary>
public static class TestData
{
    public const string GatewayName = "gw";
    public const string GatewayNamespace = "infra";
    public const string RouteNamespace = "shop";

    public static Gateway Gateway(params Listener[] listeners) => new()
    {
        Metadata = new ObjectMeta { Name = GatewayName, Namespace = GatewayNamespace },
        Spec = new GatewaySpec { Listeners = listeners.ToList() },
    };

    /// <summary>A listener that allows HTTPRoutes from all namespaces.</summary>
    public static Listener Listener(string protocol = "HTTPS", int port = 443, string? hostname = null, string name = "web") => new()
    {
        Name = name,
        Protocol = protocol,
        Port = port,
        Hostname = hostname,
        AllowedRoutes = new AllowedRoutes { Namespaces = new RouteNamespaces { From = "All" } },
    };

    public static Dictionary<string, string> Labels(string? discoverable = "true", string? domain = "shop")
    {
        var labels = new Dictionary<string, string>();
        if (discoverable is not null) labels["discoverable"] = discoverable;
        if (domain is not null) labels["domain"] = domain;
        return labels;
    }

    public static HttpRouteMatch Prefix(string value) =>
        new() { Path = new HttpPathMatch { Type = "PathPrefix", Value = value } };

    public static HttpRouteMatch Exact(string value) =>
        new() { Path = new HttpPathMatch { Type = "Exact", Value = value } };

    public static HttpRouteMatch Regex(string value) =>
        new() { Path = new HttpPathMatch { Type = "RegularExpression", Value = value } };

    /// <summary>
    /// Builds an HTTPRoute that points to the default Gateway.
    /// matches = null → the route has no rules (so no path).
    /// accepted = null → the route has no status at all.
    /// </summary>
    public static HttpRoute Route(
        string name = "route",
        Dictionary<string, string>? labels = null,
        string[]? hostnames = null,
        HttpRouteMatch[]? matches = null,
        bool? accepted = true)
    {
        var parent = new ParentReference { Name = GatewayName, Namespace = GatewayNamespace };

        var route = new HttpRoute
        {
            Metadata = new ObjectMeta { Name = name, Namespace = RouteNamespace, Labels = labels ?? Labels() },
            Spec = new HttpRouteSpec
            {
                ParentRefs = [parent],
                Hostnames = hostnames?.ToList(),
                Rules = matches is null ? null : [new HttpRouteRule { Matches = matches.ToList() }],
            },
        };

        if (accepted is not null)
        {
            route.Status = new HttpRouteStatus
            {
                Parents =
                [
                    new RouteParentStatus
                    {
                        ParentRef = new ParentReference { Name = GatewayName, Namespace = GatewayNamespace },
                        Conditions = [new Condition { Type = "Accepted", Status = accepted.Value ? "True" : "False" }],
                    },
                ],
            };
        }

        return route;
    }

    /// <summary>Runs the feature like the endpoint does and returns the answer.</summary>
    public static Task<DiscoverableRoutesResponse> Run(FakeGatewayApiReader cluster) =>
        new DiscoverableRouteService(cluster).GetAsync(CancellationToken.None);

    public static IReadOnlyList<string> AllUrls(DiscoverableRoutesResponse response) =>
        response.Domains.SelectMany(d => d.Urls).ToList();
}
