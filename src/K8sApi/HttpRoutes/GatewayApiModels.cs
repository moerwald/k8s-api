using System.Text.Json.Serialization;

namespace K8sApi.HttpRoutes;

// Small models with only the Gateway API fields we need (gateway.networking.k8s.io/v1).
// Unknown fields in the JSON are ignored.

public sealed class ObjectMeta
{
    public string Name { get; set; } = "";
    public string Namespace { get; set; } = "";
    public Dictionary<string, string>? Labels { get; set; }
}

public sealed class ResourceList<T>
{
    public List<T> Items { get; set; } = new();
    public ListMeta? Metadata { get; set; }
}

public sealed class ListMeta
{
    [JsonPropertyName("continue")]
    public string? Continue { get; set; }
}

// ---------- HTTPRoute ----------

public sealed class HttpRoute
{
    public ObjectMeta Metadata { get; set; } = new();
    public HttpRouteSpec Spec { get; set; } = new();
    public HttpRouteStatus? Status { get; set; }
}

public sealed class HttpRouteSpec
{
    public List<ParentReference>? ParentRefs { get; set; }
    public List<string>? Hostnames { get; set; }
    public List<HttpRouteRule>? Rules { get; set; }
}

public sealed class ParentReference
{
    public string? Group { get; set; }
    public string? Kind { get; set; }
    public string? Namespace { get; set; }
    public string Name { get; set; } = "";
    public string? SectionName { get; set; }
    public int? Port { get; set; }
}

public sealed class HttpRouteRule
{
    public List<HttpRouteMatch>? Matches { get; set; }
}

public sealed class HttpRouteMatch
{
    public HttpPathMatch? Path { get; set; }
}

public sealed class HttpPathMatch
{
    public string? Type { get; set; }
    public string? Value { get; set; }
}

public sealed class HttpRouteStatus
{
    public List<RouteParentStatus>? Parents { get; set; }
}

public sealed class RouteParentStatus
{
    public ParentReference ParentRef { get; set; } = new();
    public List<Condition>? Conditions { get; set; }
}

public sealed class Condition
{
    public string Type { get; set; } = "";
    public string Status { get; set; } = "";
}

// ---------- Gateway ----------

public sealed class Gateway
{
    public ObjectMeta Metadata { get; set; } = new();
    public GatewaySpec Spec { get; set; } = new();
}

public sealed class GatewaySpec
{
    public List<Listener>? Listeners { get; set; }
}

public sealed class Listener
{
    public string Name { get; set; } = "";
    public string? Hostname { get; set; }
    public int Port { get; set; }
    public string Protocol { get; set; } = "";
    public AllowedRoutes? AllowedRoutes { get; set; }
}

public sealed class AllowedRoutes
{
    public RouteNamespaces? Namespaces { get; set; }
    public List<RouteGroupKind>? Kinds { get; set; }
}

public sealed class RouteNamespaces
{
    /// <summary>All, Same (default) or Selector.</summary>
    public string? From { get; set; }
}

public sealed class RouteGroupKind
{
    public string? Group { get; set; }
    public string Kind { get; set; } = "";
}

// ---------- API response ----------

public sealed record DiscoverableRoutesResponse(IReadOnlyList<DomainUrls> Domains);

public sealed record DomainUrls(string Domain, IReadOnlyList<string> Urls);
