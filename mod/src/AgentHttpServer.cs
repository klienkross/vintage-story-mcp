using System.Net;
using System.Text;
using System.Text.Json;
using VintageStoryAgent.Protocol;

namespace VintageStoryAgent;

public sealed record AgentHttpServerOptions(string Prefix, TimeSpan StateRequestTimeout)
{
    public const int DefaultPort = 42420;
    public static readonly TimeSpan DefaultStateRequestTimeout = TimeSpan.FromSeconds(2);

    public static AgentHttpServerOptions CreateDefault() => ForLoopback(DefaultPort, DefaultStateRequestTimeout);

    public static AgentHttpServerOptions ForLoopback(int port) => ForLoopback(port, DefaultStateRequestTimeout);

    public static AgentHttpServerOptions ForLoopback(int port, TimeSpan stateRequestTimeout)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (stateRequestTimeout <= TimeSpan.Zero || stateRequestTimeout == Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(stateRequestTimeout), "State request timeout must be finite and greater than zero.");
        }

        return new AgentHttpServerOptions($"http://127.0.0.1:{port}/", stateRequestTimeout);
    }
}

public sealed class AgentHttpServer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly HttpListener listener = new();
    private readonly IAgentStateProvider stateProvider;
    private readonly TimeSpan stateRequestTimeout;
    private readonly CancellationTokenSource shutdown = new();
    private Task? acceptLoop;
    private int disposed;

    public AgentHttpServer(AgentHttpServerOptions options, IAgentStateProvider stateProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.stateProvider = stateProvider ?? throw new ArgumentNullException(nameof(stateProvider));
        stateRequestTimeout = options.StateRequestTimeout;
        listener.Prefixes.Add(options.Prefix);
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (listener.IsListening) return;
        listener.Start();
        acceptLoop = Task.Run(AcceptLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        while (!shutdown.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().WaitAsync(shutdown.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
            {
                break;
            }
            catch (HttpListenerException) when (shutdown.IsCancellationRequested || !listener.IsListening)
            {
                break;
            }
            catch (ObjectDisposedException) when (shutdown.IsCancellationRequested)
            {
                break;
            }

            _ = HandleRequestAsync(context);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            if (!string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(context.Response, 405, AgentError.MethodNotAllowed).ConfigureAwait(false);
                return;
            }

            if (!string.Equals(context.Request.Url?.AbsolutePath, "/v1/state", StringComparison.Ordinal))
            {
                await WriteJsonAsync(context.Response, 404, AgentError.NotFound).ConfigureAwait(false);
                return;
            }

            using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
            requestCancellation.CancelAfter(stateRequestTimeout);

            AgentStateResult result;
            try
            {
                result = await stateProvider.GetStateAsync(requestCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
            {
                await WriteJsonAsync(context.Response, 503, AgentError.StateTimeout).ConfigureAwait(false);
                return;
            }

            if (result.IsSuccess)
            {
                await WriteJsonAsync(context.Response, 200, result.State!).ConfigureAwait(false);
            }
            else
            {
                await WriteJsonAsync(context.Response, 503, result.Error ?? AgentError.ExecutionError).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            SafeClose(context.Response);
        }
        catch
        {
            try
            {
                await WriteJsonAsync(context.Response, 500, AgentError.ExecutionError).ConfigureAwait(false);
            }
            catch
            {
                SafeClose(context.Response);
            }
        }
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, int statusCode, object payload)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(payload, payload.GetType(), JsonOptions);
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentEncoding = Encoding.UTF8;
        response.ContentLength64 = body.Length;
        await response.OutputStream.WriteAsync(body).ConfigureAwait(false);
        response.Close();
    }

    private static void SafeClose(HttpListenerResponse response)
    {
        try { response.Close(); } catch { }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        shutdown.Cancel();
        if (listener.IsListening) listener.Stop();
        listener.Close();
        try { acceptLoop?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        shutdown.Dispose();
    }
}
