using K8sApi.HttpRoutes;
using static K8sApi.Tests.HttpRoutes.TestData;

namespace K8sApi.Tests.HttpRoutes;

/// <summary>Spec rules L1–L2: logging of routes without URL. See docs/features/01-discoverable-httproutes.md.</summary>
public class LoggingRuleTests
{
    private static async Task<ListLogger<DiscoverableRouteService>> LogFor(Listener listener, params HttpRoute[] routes)
    {
        var cluster = new FakeGatewayApiReader();
        cluster.Gateways.Add(Gateway(listener));
        cluster.Routes.AddRange(routes);

        var logger = new ListLogger<DiscoverableRouteService>();
        await Run(cluster, logger);
        return logger;
    }

    [Fact]
    public async Task L1_route_with_wildcard_hostname_logs_warning_with_name_and_reason()
    {
        var logger = await LogFor(Listener(), Route("cart", hostnames: ["*.example.com"]));

        var warning = Assert.Single(logger.Warnings);
        Assert.Contains($"{RouteNamespace}/cart", warning.Message);
        Assert.Contains("wildcard", warning.Message, StringComparison.OrdinalIgnoreCase);
    }

    public static TheoryData<string, Listener, HttpRoute> RoutesWithoutUrl => new()
    {
        { "no hostname", Listener(hostname: null), Route("cart", hostnames: null) },
        { "regex path only", Listener(), Route("cart", hostnames: ["shop.example.com"], matches: [Regex("/item/.*")]) },
        { "not accepted", Listener(), Route("cart", hostnames: ["shop.example.com"], accepted: false) },
        { "no status", Listener(), Route("cart", hostnames: ["shop.example.com"], accepted: null) },
    };

    [Theory]
    [MemberData(nameof(RoutesWithoutUrl))]
    public async Task L1_route_without_url_logs_one_warning_with_name(string scenario, Listener listener, HttpRoute route)
    {
        var logger = await LogFor(listener, route);

        var warning = Assert.Single(logger.Warnings);
        Assert.True(warning.Message.Contains($"{RouteNamespace}/cart"),
            $"Scenario '{scenario}': log message has no route name: {warning.Message}");
    }

    [Fact]
    public async Task L2_route_with_url_logs_no_warning()
    {
        var logger = await LogFor(Listener(), Route("cart", hostnames: ["shop.example.com"]));

        Assert.Empty(logger.Warnings);
    }
}
