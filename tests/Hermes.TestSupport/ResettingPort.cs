using System.Net;
using System.Net.Sockets;

namespace Hermes.TestSupport;

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
