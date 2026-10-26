using System.Text.Json;
using K8sApi.HttpRoutes;
using static K8sApi.Tests.HttpRoutes.TestData;

namespace K8sApi.Tests.HttpRoutes;

/// <summary>Spec rules R1–R3: shape and order of the answer. See docs/features/01-discoverable-httproutes.md.</summary>
public class ResponseRuleTests
{
    private static FakeGatewayApiReader Cluster(params HttpRoute[] routes)
    {
        var cluster = new FakeGatewayApiReader();
        cluster.Gateways.Add(Gateway(Listener()));
        cluster.Routes.AddRange(routes);
        return cluster;
    }

    [Fact]
    public async Task R1_routes_are_grouped_by_domain()
    {
        var response = await Run(Cluster(
            Route("cart", Labels(domain: "shop"), ["shop.example.com"], [Prefix("/cart")]),
            Route("checkout", Labels(domain: "shop"), ["shop.example.com"], [Prefix("/checkout")]),
            Route("blog", Labels(domain: "blog"), ["blog.example.com"])));

        Assert.Equal(2, response.Domains.Count);

        var shop = Assert.Single(response.Domains, d => d.Domain == "shop");
        Assert.Equal(new[] { "https://shop.example.com/cart", "https://shop.example.com/checkout" }, shop.Urls);

        var blog = Assert.Single(response.Domains, d => d.Domain == "blog");
        Assert.Equal(new[] { "https://blog.example.com/" }, blog.Urls);
    }

    [Fact]
    public async Task R2_domains_and_urls_are_sorted_a_to_z()
    {
        var response = await Run(Cluster(
            Route("z", Labels(domain: "zoo"), ["zoo.example.com"]),
            Route("a", Labels(domain: "alpha"), ["alpha.example.com"]),
            Route("m", Labels(domain: "mid"), ["mid.example.com"], [Prefix("/c"), Prefix("/a"), Prefix("/b")])));

        Assert.Equal(new[] { "alpha", "mid", "zoo" }, response.Domains.Select(d => d.Domain));

        var mid = response.Domains.Single(d => d.Domain == "mid");
        Assert.Equal(
            new[] { "https://mid.example.com/a", "https://mid.example.com/b", "https://mid.example.com/c" },
            mid.Urls);
    }

    [Fact]
    public void R3_json_uses_camel_case_names()
    {
        // ASP.NET Core minimal APIs serialize with the "Web" defaults.
        var response = new DiscoverableRoutesResponse(
            [new DomainUrls("shop", ["https://shop.example.com/cart"])]);

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("""{"domains":[{"domain":"shop","urls":["https://shop.example.com/cart"]}]}""", json);
    }
}
