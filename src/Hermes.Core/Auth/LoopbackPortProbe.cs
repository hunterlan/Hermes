using System.Net;
using System.Net.Sockets;

namespace Hermes.Core.Auth;

/// <summary>
/// Finds out whether another program holds a port on a loopback address. HttpListener binds only one of the loopback
/// addresses for "localhost" (IPv4 on macOS, IPv6 on Linux), so it doesn't see a program on the other one, and the
/// browser's redirect could then reach that program instead of Hermes.
/// </summary>
internal static class LoopbackPortProbe
{
    /// <summary>The socket error with which the first loopback address that is in use fails, or null when the port is free on all of them.</summary>
    public static SocketException? FindConflict(int port)
    {
        foreach (var address in LoopbackAddresses())
        {
            try
            {
                var listener = new TcpListener(address, port);
                listener.Start();
                listener.Stop();
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                return ex;
            }
            catch (SocketException)
            {
                // The address can't be used at all, e.g. no IPv6 on this computer. It can't clash with anything either.
            }
        }
        return null;
    }

    private static IEnumerable<IPAddress> LoopbackAddresses()
    {
        yield return IPAddress.Loopback;
        if (Socket.OSSupportsIPv6)
            yield return IPAddress.IPv6Loopback;
    }
}
