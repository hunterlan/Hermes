using System.Net;
using System.Net.Sockets;

namespace Hermes.TestSupport;

/// <summary>
/// HttpListener on a loopback port, serving each request with <c>handler</c>. The port is claimed by starting the
/// listener itself (retrying on a clash), so no other process can take it between picking and binding.
/// </summary>
public sealed class LocalHttpServer : IDisposable
{
    private const int MaxAttempts = 20;

    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _stop = new();

    public LocalHttpServer(Func<HttpListenerContext, CancellationToken, Task> handler)
    {
        (_listener, Url) = Listen();
        _ = Task.Run(() => ServeAsync(handler));
    }

    public string Url { get; }

    private static (HttpListener Listener, string Url) Listen()
    {
        for (var attempt = 1; ; attempt++)
        {
            var url = $"http://127.0.0.1:{Random.Shared.Next(20000, 60000)}/";
            var listener = new HttpListener();
            listener.Prefixes.Add(url);
            try
            {
                listener.Start();
                return (listener, url);
            }
            catch (HttpListenerException) when (attempt < MaxAttempts)
            {
                listener.Close();
            }
        }
    }

    private async Task ServeAsync(Func<HttpListenerContext, CancellationToken, Task> handler)
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or InvalidOperationException) { return; }

            _ = Task.Run(async () =>
            {
                // A failing handler drops the connection, so the client sees an error instead of hanging.
                try { await handler(ctx, _stop.Token); }
                catch (Exception) { ctx.Response.Abort(); }
            });
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Close();
    }
}

/// <summary>
/// A loopback port, held for the lifetime of this object, that resets every connection. (A port that is only bound
/// isn't refused on macOS: connecting to it waits until the client times out.)
/// </summary>
public sealed class ResettingPort : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

    public ResettingPort()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(async () =>
        {
            while (true)
            {
                Socket socket;
                try { socket = await _listener.AcceptSocketAsync(); }
                catch (Exception e) when (e is SocketException or ObjectDisposedException) { return; }
                socket.LingerState = new LingerOption(true, 0);
                socket.Dispose();
            }
        });
    }

    public int Port { get; }

    public void Dispose() => _listener.Stop();
}
