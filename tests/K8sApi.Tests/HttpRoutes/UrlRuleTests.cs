using K8sApi.HttpRoutes;
using static K8sApi.Tests.HttpRoutes.TestData;

namespace K8sApi.Tests.HttpRoutes;

/// <summary>Spec rules U1–U8: how a URL is built. See docs/features/01-discoverable-httproutes.md.</summary>
public class UrlRuleTests
{
    private static async Task<IReadOnlyList<string>> UrlsFor(Listener listener, params HttpRoute[] routes)
    {
        var cluster = new FakeGatewayApiReader();
        cluster.Gateways.Add(Gateway(listener));
        cluster.Routes.AddRange(routes);
        return AllUrls(await Run(cluster));
    }

    // ---------- U1: scheme ----------

    [Theory]
    [InlineData("HTTPS", 443, "https://shop.example.com/")]
    [InlineData("HTTP", 80, "http://shop.example.com/")]
    public async Task U1_scheme_comes_from_listener_protocol(string protocol, int port, string expected)
    {
        var urls = await UrlsFor(Listener(protocol, port), Route(hostnames: ["shop.example.com"]));

        Assert.Equal(new[] { expected }, urls);
    }

    // ---------- U2: port ----------

    [Theory]
    [InlineData("HTTPS", 8443, "https://shop.example.com:8443/")]
    [InlineData("HTTP", 8080, "http://shop.example.com:8080/")]
    [InlineData("HTTPS", 80, "https://shop.example.com:80/")]
    [InlineData("HTTP", 443, "http://shop.example.com:443/")]
    public async Task U2_port_is_shown_when_not_default(string protocol, int port, string expected)
    {
        var urls = await UrlsFor(Listener(protocol, port), Route(hostnames: ["shop.example.com"]));

        Assert.Equal(new[] { expected }, urls);
    }

    // ---------- U3: hostname ----------

    [Fact]
    public async Task U3_route_without_hostname_uses_listener_hostname()
    {
        var urls = await UrlsFor(Listener(hostname: "shop.example.com"), Route(hostnames: null));

        Assert.Equal(new[] { "https://shop.example.com/" }, urls);
    }

    [Fact]
    public async Task U3_no_hostname_on_route_and_listener_gives_no_url()
    {
        var urls = await UrlsFor(Listener(hostname: null), Route(hostnames: null));

        Assert.Empty(urls);
    }

    // ---------- U4: wildcard ----------

    [Fact]
    public async Task U4_wildcard_route_hostname_gives_no_url()
    {
        var urls = await UrlsFor(Listener(hostname: null), Route(hostnames: ["*.example.com"]));

        Assert.Empty(urls);
    }

    [Fact]
    public async Task U4_wildcard_listener_hostname_gives_no_url()
    {
        var urls = await UrlsFor(Listener(hostname: "*.example.com"), Route(hostnames: null));

        Assert.Empty(urls);
    }

    // ---------- U5: no path ----------

    [Fact]
    public async Task U5_route_without_rules_uses_slash()
    {
        var urls = await UrlsFor(Listener(), Route(hostnames: ["shop.example.com"], matches: null));

        Assert.Equal(new[] { "https://shop.example.com/" }, urls);
    }

    [Fact]
    public async Task U5_match_without_path_uses_slash()
    {
        var urls = await UrlsFor(Listener(), Route(hostnames: ["shop.example.com"], matches: [new HttpRouteMatch()]));

        Assert.Equal(new[] { "https://shop.example.com/" }, urls);
    }

    [Fact]
    public async Task U5_prefix_and_exact_paths_are_used()
    {
        var urls = await UrlsFor(Listener(),
            Route(hostnames: ["shop.example.com"], matches: [Prefix("/cart"), Exact("/checkout")]));

        Assert.Equal(new[] { "https://shop.example.com/cart", "https://shop.example.com/checkout" }, urls);
    }

    // ---------- U6: regex path ----------

    [Fact]
    public async Task U6_regex_path_is_skipped_other_paths_stay()
    {
        var urls = await UrlsFor(Listener(),
            Route(hostnames: ["shop.example.com"], matches: [Prefix("/cart"), Regex("/item/.*")]));

        Assert.Equal(new[] { "https://shop.example.com/cart" }, urls);
    }

    // ---------- U7: accepted status ----------

    [Fact]
    public async Task U7_route_not_accepted_gives_no_url()
    {
        var urls = await UrlsFor(Listener(), Route(hostnames: ["shop.example.com"], accepted: false));

        Assert.Empty(urls);
    }

    [Fact]
    public async Task U7_route_without_status_gives_no_url()
    {
        var urls = await UrlsFor(Listener(), Route(hostnames: ["shop.example.com"], accepted: null));

        Assert.Empty(urls);
    }

    // ---------- U8: duplicates ----------

    [Fact]
    public async Task U8_same_url_from_two_routes_is_shown_once()
    {
        var urls = await UrlsFor(Listener(),
            Route("a", hostnames: ["shop.example.com"], matches: [Prefix("/cart")]),
            Route("b", hostnames: ["shop.example.com"], matches: [Prefix("/cart")]));

        Assert.Equal(new[] { "https://shop.example.com/cart" }, urls);
    }
}
