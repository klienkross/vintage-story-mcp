using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using VintageStoryAgent;
using VintageStoryAgent.Protocol;
using Vintagestory.API.Client;

var tests = new (string Name, Func<Task> Run)[]
{
    ("state success serialization with null block", StateSuccessWithNullBlock),
    ("state success serialization with selected block", StateSuccessWithBlock),
    ("player unavailable stable contract", PlayerUnavailable),
    ("server options bind loopback only", LoopbackOnly),
    ("snapshot provider crosses dispatcher seam", SnapshotUsesDispatcher),
    ("state request timeout cancels provider", StateRequestTimeoutCancelsProvider),
    ("dispatcher skips late action after cancellation", DispatcherSkipsLateActionAfterCancellation),
    ("dispose stops accepting requests", DisposeStopsRequests)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failures.Add($"FAIL {test.Name}: {ex.Message}");
        Console.Error.WriteLine(failures[^1]);
    }
}

if (failures.Count > 0)
{
    Environment.ExitCode = 1;
    return;
}

Console.WriteLine($"PASS {tests.Length} tests");

static async Task StateSuccessWithNullBlock()
{
    var state = new AgentState(true, new PlayerState(new Position3d(123.4, 82, -45.2), 1.23f, -0.18f), new SelectionState(null));
    await WithServer(AgentStateResult.Success(state), async baseUri =>
    {
        using var client = new HttpClient();
        using var response = await client.GetAsync(new Uri(baseUri, "v1/state"));
        Assert(response.StatusCode == HttpStatusCode.OK, "expected HTTP 200");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert(doc.RootElement.GetProperty("ok").GetBoolean(), "ok must be true");
        Assert(doc.RootElement.GetProperty("selection").GetProperty("block").ValueKind == JsonValueKind.Null, "block must be null");
    });
}

static async Task StateSuccessWithBlock()
{
    var block = new BlockSelectionState(new BlockPosition(124, 82, -46), "game:granite");
    var state = new AgentState(true, new PlayerState(new Position3d(123.4, 82, -45.2), 1.23f, -0.18f), new SelectionState(block));
    await WithServer(AgentStateResult.Success(state), async baseUri =>
    {
        using var client = new HttpClient();
        using var response = await client.GetAsync(new Uri(baseUri, "v1/state"));
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var selected = doc.RootElement.GetProperty("selection").GetProperty("block");
        Assert(selected.GetProperty("position").GetProperty("x").GetInt32() == 124, "selected x mismatch");
        Assert(selected.GetProperty("code").GetString() == "game:granite", "selected code mismatch");
    });
}

static async Task PlayerUnavailable()
{
    await WithServer(AgentStateResult.Failure(AgentError.PlayerUnavailable), async baseUri =>
    {
        using var client = new HttpClient();
        using var response = await client.GetAsync(new Uri(baseUri, "v1/state"));
        Assert(response.StatusCode == HttpStatusCode.ServiceUnavailable, "expected HTTP 503");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert(!doc.RootElement.GetProperty("ok").GetBoolean(), "ok must be false");
        Assert(doc.RootElement.GetProperty("code").GetString() == "player_unavailable", "wrong error code");
    });
}

static Task LoopbackOnly()
{
    var options = AgentHttpServerOptions.ForLoopback(42420);
    Assert(options.Prefix == "http://127.0.0.1:42420/", "prefix must be explicit IPv4 loopback");
    Assert(!options.Prefix.Contains("0.0.0.0"), "must not bind all IPv4 interfaces");
    Assert(!options.Prefix.Contains("[::]"), "must not bind all IPv6 interfaces");
    return Task.CompletedTask;
}

static async Task SnapshotUsesDispatcher()
{
    var dispatcher = new RecordingDispatcher();
    var reader = new FixedReader(AgentStateResult.Failure(AgentError.PlayerUnavailable));
    var provider = new AgentStateSnapshotProvider(dispatcher, reader);
    var result = await provider.GetStateAsync(CancellationToken.None);
    Assert(dispatcher.InvocationCount == 1, "reader must be invoked through dispatcher");
    Assert(reader.ReadCount == 1, "reader must be called once");
    Assert(result.Error?.Code == "player_unavailable", "result must propagate");
}

static async Task StateRequestTimeoutCancelsProvider()
{
    int port = ReservePort();
    var provider = new NeverCompletingProvider();
    var options = AgentHttpServerOptions.ForLoopback(port, TimeSpan.FromMilliseconds(150));
    using var server = new AgentHttpServer(options, provider);
    server.Start();

    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    var stopwatch = Stopwatch.StartNew();
    using var response = await client.GetAsync(new Uri($"http://127.0.0.1:{port}/v1/state"));
    stopwatch.Stop();

    Assert(response.StatusCode == HttpStatusCode.ServiceUnavailable, "state timeout must return HTTP 503");
    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    Assert(doc.RootElement.GetProperty("code").GetString() == "state_timeout", "wrong timeout error code");
    Assert(stopwatch.Elapsed < TimeSpan.FromSeconds(1), "server must enforce its own bounded request deadline");
    await provider.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));
}

static async Task DispatcherSkipsLateActionAfterCancellation()
{
    var api = new QueuedClientApi();
    var dispatcher = new VintageStoryMainThreadDispatcher(api);
    using var cts = new CancellationTokenSource();
    int readCount = 0;

    ValueTask<int> pending = dispatcher.InvokeAsync(() =>
    {
        readCount++;
        return 42;
    }, cts.Token);

    Assert(api.Events.PendingCount == 1, "main-thread callback must be queued");
    cts.Cancel();

    bool canceled = false;
    try { _ = await pending.AsTask(); }
    catch (OperationCanceledException) { canceled = true; }
    Assert(canceled, "dispatcher completion must observe cancellation");

    api.Events.RunNext();
    Assert(readCount == 0, "late main-thread callback must not execute reader action after cancellation");
}

static async Task DisposeStopsRequests()
{
    int port = ReservePort();
    var server = new AgentHttpServer(AgentHttpServerOptions.ForLoopback(port), new FixedProvider(AgentStateResult.Failure(AgentError.PlayerUnavailable)));
    server.Start();
    var uri = new Uri($"http://127.0.0.1:{port}/v1/state");
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
    using var first = await client.GetAsync(uri);
    Assert(first.StatusCode == HttpStatusCode.ServiceUnavailable, "pre-dispose request should succeed at transport layer");
    server.Dispose();
    bool refused = false;
    try { using var _ = await client.GetAsync(uri); }
    catch (HttpRequestException) { refused = true; }
    catch (TaskCanceledException) { refused = true; }
    Assert(refused, "disposed server must stop accepting requests");
}

static async Task WithServer(AgentStateResult result, Func<Uri, Task> body)
{
    int port = ReservePort();
    using var server = new AgentHttpServer(AgentHttpServerOptions.ForLoopback(port), new FixedProvider(result));
    server.Start();
    await body(new Uri($"http://127.0.0.1:{port}/"));
}

static int ReservePort()
{
    var socket = new TcpListener(IPAddress.Loopback, 0);
    socket.Start();
    int port = ((IPEndPoint)socket.LocalEndpoint).Port;
    socket.Stop();
    return port;
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class FixedProvider(AgentStateResult result) : IAgentStateProvider
{
    public ValueTask<AgentStateResult> GetStateAsync(CancellationToken cancellationToken) => ValueTask.FromResult(result);
}

sealed class NeverCompletingProvider : IAgentStateProvider
{
    public TaskCompletionSource<bool> CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async ValueTask<AgentStateResult> GetStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CancellationObserved.TrySetResult(true);
            throw;
        }
    }
}

sealed class FixedReader(AgentStateResult result) : IAgentStateReader
{
    public int ReadCount { get; private set; }
    public AgentStateResult ReadState() { ReadCount++; return result; }
}

sealed class RecordingDispatcher : IMainThreadDispatcher
{
    public int InvocationCount { get; private set; }
    public ValueTask<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        InvocationCount++;
        return ValueTask.FromResult(action());
    }
}

sealed class QueuedClientApi : ICoreClientAPI
{
    public QueuedClientEvents Events { get; } = new();
    public IClientEventAPI Event => Events;
}

sealed class QueuedClientEvents : IClientEventAPI
{
    private readonly Queue<Action> pending = new();
    public int PendingCount => pending.Count;

    public void EnqueueMainThreadTask(Action action, string code) => pending.Enqueue(action);

    public void RunNext()
    {
        if (pending.Count == 0) throw new InvalidOperationException("no queued callback");
        pending.Dequeue()();
    }
}
