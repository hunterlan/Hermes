using System.Net;
using System.Net.Sockets;

namespace Hermes.TestSupport;

/// <summary>A loopback port that nothing listens on, so connecting to it is refused.</summary>
public static class ClosedPort
{
    /// <summary>Takes a free port and releases it again. Another process could claim it in between, which is unlikely.</summary>
    public static int Find()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
