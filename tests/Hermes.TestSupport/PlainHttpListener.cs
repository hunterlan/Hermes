using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Hermes.TestSupport;

/// <summary>
/// A loopback listener that answers every connection with <c>400 Bad Request</c> in plain HTTP, whatever it receives.
/// An HTTPS request to it fails the TLS handshake, as it does against a server that only speaks plain HTTP.
/// The port is claimed by binding to port 0, so nothing can take it first.
/// </summary>
public sealed class PlainHttpListener : IDisposable
{
    private static readonly byte[] Response = Encoding.ASCII.GetBytes("HTTP/1.1 400 Bad Request\r\n\r\n");

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();

    public PlainHttpListener()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(AcceptAsync);
    }

    public int Port { get; }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException) { return; }
            _ = Task.Run(() => AnswerAsync(client));
        }
    }

    private async Task AnswerAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                await client.GetStream().WriteAsync(Response, _stop.Token);
                // Keep the connection open a moment so the client reads the answer before it sees a close.
                await Task.Delay(500, _stop.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException or SocketException) { }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        _stop.Dispose();
    }
}
