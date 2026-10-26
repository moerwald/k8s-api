using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using k8s.Autorest;
using K8sApi.Tests.Api;
using Microsoft.Extensions.Logging;

namespace K8sApi.Tests.HttpRoutes;

/// <summary>
/// Spec rules E1–E11: errors of GET /api/v1/httproutes/discoverable.
/// See docs/features/01-discoverable-httproutes.md.
/// The real API runs in memory with a fake Kubernetes cluster that fails on purpose.
/// </summary>
public class ErrorRuleTests
{
    private const string Url = "/api/v1/httproutes/discoverable";

    // Text that must never reach the client (spec E10). Every fake error contains it.
    private const string Secret = "SECRET-DETAIL";
    private const string ClusterAddress = "10.1.2.3";

    /// <summary>Fake failures, like the Kubernetes client really throws them.</summary>
    private static Exception MakeError(string id) => id switch
    {
        "E1" => new HttpRequestException($"{Secret} connection refused https://{ClusterAddress}:6443",
                    new SocketException((int)SocketError.ConnectionRefused)),
        "E2" => KubernetesError(HttpStatusCode.Forbidden),
        "E3" => KubernetesError(HttpStatusCode.Unauthorized),
        "E4" => KubernetesError(HttpStatusCode.NotFound), // HTTPRoute CRD missing → 404
        "E6" => new JsonException($"{Secret} invalid JSON from https://{ClusterAddress}:6443"),
        "E7" => new InvalidOperationException($"{Secret} bug in the API"),
        _ => throw new ArgumentOutOfRangeException(nameof(id)),
    };

    private static HttpOperationException KubernetesError(HttpStatusCode code) =>
        new($"{Secret} Operation returned an invalid status code '{code}' https://{ClusterAddress}:6443")
        {
            Response = new HttpResponseMessageWrapper(
                new HttpResponseMessage(code),
                $$"""{"kind":"Status","message":"{{Secret}} from https://{{ClusterAddress}}:6443","code":{{(int)code}}}"""),
        };

    private sealed record Call(ApiFactory Api, HttpResponseMessage Response, string Body, Exception? Thrown, TimeSpan Elapsed);

    /// <summary>Calls the endpoint while the fake cluster fails like case <paramref name="id"/>.</summary>
    private static async Task<Call> CallWithError(string id)
    {
        var api = new ApiFactory();
        Exception? thrown = null;

        if (id == "E5")
        {
            // Kubernetes never answers.
            api.Cluster.OnCall = ct => Task.Delay(Timeout.Infinite, ct);
        }
        else
        {
            var error = MakeError(id);
            thrown = error;
            api.Cluster.OnCall = _ => throw error;
        }

        var client = api.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30); // so a missing API timeout fails the test instead of hanging

        var watch = Stopwatch.StartNew();
        var response = await client.GetAsync(Url);
        watch.Stop();

        return new Call(api, response, await response.Content.ReadAsStringAsync(), thrown, watch.Elapsed);
    }

    public static TheoryData<string> AllErrorCases => new() { "E1", "E2", "E3", "E4", "E5", "E6", "E7" };

    // ---------- E1–E7: status and title ----------

    [Theory]
    [InlineData("E1", 502, "Kubernetes API not reachable")]
    [InlineData("E2", 502, "Kubernetes API access forbidden")]
    [InlineData("E3", 502, "Kubernetes API authentication failed")]
    [InlineData("E4", 502, "Gateway API not installed")]
    [InlineData("E6", 502, "Kubernetes API sent invalid data")]
    [InlineData("E7", 500, "Internal server error")]
    public async Task E1_to_E7_error_gives_status_and_title(string id, int status, string title)
    {
        var call = await CallWithError(id);
        using var _ = call.Api;

        Assert.Equal(status, (int)call.Response.StatusCode);
        Assert.Equal(title, ReadProblem(call.Body).GetProperty("title").GetString());
    }

    [Fact]
    public async Task E5_slow_kubernetes_gives_504_after_at_most_10_seconds()
    {
        var call = await CallWithError("E5");
        using var _ = call.Api;

        Assert.Equal(504, (int)call.Response.StatusCode);
        Assert.Equal("Kubernetes API timeout", ReadProblem(call.Body).GetProperty("title").GetString());
        // 10 seconds plus a little time for the test machine.
        Assert.True(call.Elapsed < TimeSpan.FromSeconds(13), $"The answer took {call.Elapsed.TotalSeconds:F1} s.");
    }

    // ---------- E9: problem details body ----------

    [Theory]
    [MemberData(nameof(AllErrorCases))]
    public async Task E9_body_is_problem_json_with_title_status_and_trace_id(string id)
    {
        var call = await CallWithError(id);
        using var _ = call.Api;

        Assert.Equal("application/problem+json", call.Response.Content.Headers.ContentType?.MediaType);

        var problem = ReadProblem(call.Body);
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("title").GetString()));
        Assert.Equal((int)call.Response.StatusCode, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }

    // ---------- E10: no internal details ----------

    [Theory]
    [MemberData(nameof(AllErrorCases))]
    public async Task E10_body_has_no_internal_details(string id)
    {
        var call = await CallWithError(id);
        using var _ = call.Api;

        Assert.DoesNotContain(Secret, call.Body);
        Assert.DoesNotContain(ClusterAddress, call.Body);
        Assert.DoesNotContain("   at ", call.Body); // stack trace line

        var names = ReadProblem(call.Body).EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "status", "title", "traceId" }, names);
    }

    // ---------- E11: error log with exception and traceId ----------

    [Theory]
    [MemberData(nameof(AllErrorCases))]
    public async Task E11_error_is_logged_with_exception_and_trace_id(string id)
    {
        var call = await CallWithError(id);
        using var _ = call.Api;

        var traceId = ReadProblem(call.Body).GetProperty("traceId").GetString()!;
        var traceIdPart = TraceIdPart(traceId);

        var errors = call.Api.Logs.Entries.Where(e => e.Level >= LogLevel.Error).ToList();
        Assert.True(errors.Count > 0, "No error was logged.");

        var match = errors.FirstOrDefault(e => e.Exception is not null && e.AllText.Contains(traceIdPart));
        Assert.True(match is not null,
            $"No error log with an exception and trace id '{traceIdPart}'. Errors:\n" +
            string.Join("\n", errors.Select(e => $"- [{e.Category}] {e.Message} | scopes: {string.Join(", ", e.Scopes)}")));

        if (call.Thrown is not null)
            Assert.Contains(call.Thrown, Chain(match!.Exception!));
    }

    // ---------- E8: client cancels ----------

    [Fact]
    public async Task E8_client_cancel_logs_information_and_no_error()
    {
        using var api = new ApiFactory();
        api.Cluster.OnCall = ct => Task.Delay(Timeout.Infinite, ct);
        var client = api.CreateClient();

        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync(Url, cancel.Token));

        // The server needs a moment to see the cancel and write its log.
        bool IsCancelInfo(CapturingLoggerProvider.Entry e) =>
            e.Level == LogLevel.Information &&
            e.Category.StartsWith("K8sApi", StringComparison.Ordinal) &&
            e.Message.Contains("cancel", StringComparison.OrdinalIgnoreCase);

        var waited = Stopwatch.StartNew();
        while (!api.Logs.Entries.Any(IsCancelInfo) && waited.Elapsed < TimeSpan.FromSeconds(5))
            await Task.Delay(100);
        await Task.Delay(300); // give a wrong error log the chance to show up too

        var entries = api.Logs.Entries;
        Assert.True(entries.Any(IsCancelInfo),
            "No Information log about the cancel. Logs:\n" +
            string.Join("\n", entries.Select(e => $"- {e.Level} [{e.Category}] {e.Message}")));
        Assert.DoesNotContain(entries, e => e.Level >= LogLevel.Error);
    }

    // ---------- helpers ----------

    private static JsonElement ReadProblem(string body)
    {
        Assert.False(string.IsNullOrWhiteSpace(body), "The answer has no body.");
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    /// <summary>"00-&lt;trace&gt;-&lt;span&gt;-01" → "&lt;trace&gt;". Other formats are used as they are.</summary>
    private static string TraceIdPart(string traceId)
    {
        var parts = traceId.Split('-');
        return parts.Length == 4 ? parts[1] : traceId;
    }

    private static IEnumerable<Exception> Chain(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
            yield return e;
    }
}
