using static K8sApi.Tests.HttpRoutes.TestData;

namespace K8sApi.Tests.HttpRoutes;

/// <summary>Spec rules D1–D6: which routes are discoverable. See docs/features/01-discoverable-httproutes.md.</summary>
public class DiscoveryRuleTests
{
    private static FakeGatewayApiReader ClusterWith(Dictionary<string, string> labels)
    {
        var cluster = new FakeGatewayApiReader();
        cluster.Gateways.Add(Gateway(Listener()));
        cluster.Routes.Add(Route(labels: labels, hostnames: ["shop.example.com"]));
        return cluster;
    }

    [Fact]
    public async Task D1_route_with_discoverable_true_and_domain_is_listed()
    {
        var response = await Run(ClusterWith(Labels(discoverable: "true", domain: "shop")));

        var domain = Assert.Single(response.Domains);
        Assert.Equal("shop", domain.Domain);
        Assert.Equal(new[] { "https://shop.example.com/" }, domain.Urls);
    }

    [Theory]
    [InlineData("TRUE")]
    [InlineData("True")]
    public async Task D2_discoverable_value_is_not_case_sensitive(string value)
    {
        var response = await Run(ClusterWith(Labels(discoverable: value, domain: "shop")));

        var domain = Assert.Single(response.Domains);
        Assert.Equal("shop", domain.Domain);
        Assert.Equal(new[] { "https://shop.example.com/" }, domain.Urls);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("yes")]
    [InlineData("")]
    public async Task D3_discoverable_with_other_value_is_not_listed(string value)
    {
        var response = await Run(ClusterWith(Labels(discoverable: value, domain: "shop")));

        Assert.Empty(AllUrls(response));
        Assert.DoesNotContain(response.Domains, d => d.Domain == "shop");
    }

    [Fact]
    public async Task D4_route_without_discoverable_label_is_not_listed()
    {
        var response = await Run(ClusterWith(Labels(discoverable: null, domain: "shop")));

        Assert.Empty(AllUrls(response));
        Assert.DoesNotContain(response.Domains, d => d.Domain == "shop");
    }

    [Fact]
    public async Task D5_route_without_domain_label_is_not_listed()
    {
        var response = await Run(ClusterWith(Labels(discoverable: "true", domain: null)));

        Assert.Empty(response.Domains);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    public async Task D6_route_with_empty_domain_is_not_listed(string domain)
    {
        var response = await Run(ClusterWith(Labels(discoverable: "true", domain: domain)));

        Assert.Empty(response.Domains);
    }

    [Fact]
    public async Task D_only_discoverable_routes_are_listed_when_mixed()
    {
        var cluster = new FakeGatewayApiReader();
        cluster.Gateways.Add(Gateway(Listener()));
        cluster.Routes.Add(Route("yes", Labels("true", "shop"), ["shop.example.com"]));
        cluster.Routes.Add(Route("no", Labels("false", "blog"), ["blog.example.com"]));

        var response = await Run(cluster);

        var domain = Assert.Single(response.Domains);
        Assert.Equal("shop", domain.Domain);
    }
}
