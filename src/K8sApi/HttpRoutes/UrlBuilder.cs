namespace K8sApi.HttpRoutes;

/// <summary>
/// Turns HTTPRoutes plus their Gateways into absolute URLs, grouped by the "domain" label.
/// Pure logic, no Kubernetes calls, so it is easy to test.
/// </summary>
public static class UrlBuilder
{
    public const string DiscoverableLabel = "discoverable";
    public const string DomainLabel = "domain";
    private const string GatewayGroup = "gateway.networking.k8s.io";

    public static DiscoverableRoutesResponse Build(IEnumerable<HttpRoute> routes, IEnumerable<Gateway> gateways)
    {
        var gatewayByKey = gateways.ToDictionary(g => Key(g.Metadata.Namespace, g.Metadata.Name));

        var result = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);

        foreach (var route in routes)
        {
            if (!IsDiscoverable(route.Metadata.Labels, out var domain))
                continue;

            if (!result.TryGetValue(domain, out var urls))
                result[domain] = urls = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var url in UrlsForRoute(route, gatewayByKey))
                urls.Add(url);
        }

        var domains = result
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new DomainUrls(kv.Key, kv.Value.ToList()))
            .ToList();

        return new DiscoverableRoutesResponse(domains);
    }

    /// <summary>
    /// Spec D1–D6: "discoverable" is "true" (any case) and "domain" is not empty or only spaces.
    /// </summary>
    private static bool IsDiscoverable(Dictionary<string, string>? labels, out string domain)
    {
        domain = "";
        if (labels is null) return false;

        if (!labels.TryGetValue(DiscoverableLabel, out var discoverable) ||
            !string.Equals(discoverable, "true", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!labels.TryGetValue(DomainLabel, out var value) || string.IsNullOrWhiteSpace(value))
            return false;

        domain = value;
        return true;
    }

    private static IEnumerable<string> UrlsForRoute(HttpRoute route, IReadOnlyDictionary<string, Gateway> gateways)
    {
        var paths = Paths(route).ToList();
        if (paths.Count == 0) yield break;

        foreach (var parent in route.Spec.ParentRefs ?? [])
        {
            // Only Gateways are parents we understand.
            if ((parent.Group ?? GatewayGroup) != GatewayGroup || (parent.Kind ?? "Gateway") != "Gateway")
                continue;

            var gwNamespace = parent.Namespace ?? route.Metadata.Namespace;

            // Use only parents where the Gateway controller accepted the route.
            if (!IsAccepted(route, parent, gwNamespace))
                continue;

            if (!gateways.TryGetValue(Key(gwNamespace, parent.Name), out var gateway))
                continue;

            foreach (var listener in MatchingListeners(gateway, parent, route.Metadata.Namespace))
            {
                var scheme = listener.Protocol == "HTTPS" ? "https" : "http";
                var portPart = IsDefaultPort(scheme, listener.Port) ? "" : $":{listener.Port}";

                foreach (var host in Hostnames(route.Spec.Hostnames, listener.Hostname))
                {
                    foreach (var path in paths)
                        yield return $"{scheme}://{host}{portPart}{path}";
                }
            }
        }
    }

    /// <summary>
    /// All paths of the route. No match or no path means "/".
    /// Regex paths are skipped, because they are not real URLs.
    /// </summary>
    private static IEnumerable<string> Paths(HttpRoute route)
    {
        var rules = route.Spec.Rules;
        if (rules is null || rules.Count == 0)
        {
            yield return "/";
            yield break;
        }

        foreach (var rule in rules)
        {
            if (rule.Matches is null || rule.Matches.Count == 0)
            {
                yield return "/";
                continue;
            }

            foreach (var match in rule.Matches)
            {
                var type = match.Path?.Type ?? "PathPrefix";
                if (type == "RegularExpression") continue;

                var value = match.Path?.Value;
                yield return string.IsNullOrEmpty(value) ? "/" : value;
            }
        }
    }

    private static bool IsAccepted(HttpRoute route, ParentReference parent, string gwNamespace)
    {
        foreach (var status in route.Status?.Parents ?? [])
        {
            var p = status.ParentRef;
            var sameParent =
                p.Name == parent.Name &&
                (p.Namespace ?? route.Metadata.Namespace) == gwNamespace &&
                p.SectionName == parent.SectionName &&
                p.Port == parent.Port;

            if (sameParent && status.Conditions?.Any(c => c.Type == "Accepted" && c.Status == "True") == true)
                return true;
        }
        return false;
    }

    private static IEnumerable<Listener> MatchingListeners(Gateway gateway, ParentReference parent, string routeNamespace)
    {
        foreach (var l in gateway.Spec.Listeners ?? [])
        {
            if (l.Protocol is not ("HTTP" or "HTTPS")) continue;
            if (parent.SectionName is not null && l.Name != parent.SectionName) continue;
            if (parent.Port is not null && l.Port != parent.Port) continue;
            if (!AllowsRoute(l, gateway.Metadata.Namespace, routeNamespace)) continue;
            yield return l;
        }
    }

    private static bool AllowsRoute(Listener l, string gatewayNamespace, string routeNamespace)
    {
        var kinds = l.AllowedRoutes?.Kinds;
        if (kinds is { Count: > 0 } &&
            !kinds.Any(k => k.Kind == "HTTPRoute" && (k.Group ?? GatewayGroup) == GatewayGroup))
            return false;

        return (l.AllowedRoutes?.Namespaces?.From ?? "Same") switch
        {
            "All" => true,
            "Same" => gatewayNamespace == routeNamespace,
            // "Selector" needs namespace labels. We trust the "Accepted" status instead.
            _ => true,
        };
    }

    /// <summary>
    /// Hostnames that really reach this route through this listener.
    /// Wildcard results (*.example.com) are skipped, because they are not real URLs.
    /// </summary>
    private static IEnumerable<string> Hostnames(List<string>? routeHosts, string? listenerHost)
    {
        var candidates = new List<string>();

        if (routeHosts is null || routeHosts.Count == 0)
        {
            if (!string.IsNullOrEmpty(listenerHost)) candidates.Add(listenerHost);
        }
        else if (string.IsNullOrEmpty(listenerHost))
        {
            candidates.AddRange(routeHosts);
        }
        else
        {
            foreach (var rh in routeHosts)
            {
                var specific = MoreSpecificIfMatch(rh, listenerHost);
                if (specific is not null) candidates.Add(specific);
            }
        }

        return candidates
            .Select(h => h.ToLowerInvariant())
            .Where(h => !h.StartsWith('*'))
            .Distinct();
    }

    /// <summary>
    /// Returns the more specific hostname if both match, otherwise null.
    /// Example: "*.example.com" and "shop.example.com" → "shop.example.com".
    /// </summary>
    private static string? MoreSpecificIfMatch(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return a;
        if (WildcardCovers(a, b)) return b;
        if (WildcardCovers(b, a)) return a;
        return null;
    }

    // "*.example.com" covers "shop.example.com" and "a.b.example.com", but not "example.com".
    private static bool WildcardCovers(string wildcard, string host)
    {
        if (!wildcard.StartsWith("*.")) return false;
        var suffix = wildcard[1..]; // ".example.com"
        return host.Length > suffix.Length &&
               host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDefaultPort(string scheme, int port) =>
        (scheme == "http" && port == 80) || (scheme == "https" && port == 443);

    private static string Key(string ns, string name) => $"{ns}/{name}";
}
