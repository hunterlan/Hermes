using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Hermes.TestSupport;

/// <summary>
/// A loopback HTTPS server that answers every request with 200 and a short text body, using the given certificate.
/// HttpListener can't serve TLS with a certificate from code on every platform, so this speaks just enough HTTP/1.1
/// over an <see cref="SslStream"/> itself. The port is claimed by binding to port 0, so nothing can take it first.
/// </summary>
public sealed class LocalHttpsServer : IDisposable
{
    public const string ResponseBody = "hello over tls";

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly X509Certificate2 _certificate;
    private readonly CancellationTokenSource _stop = new();

    public LocalHttpsServer(X509Certificate2 certificate)
    {
        _certificate = certificate;
        _listener.Start();
        Url = $"https://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
        _ = Task.Run(AcceptAsync);
    }

    public string Url { get; }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException) { return; }
            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using var _ = client;
        try
        {
            await using var stream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
            await stream.AuthenticateAsServerAsync(_certificate);
            await ReadRequestHeadAsync(stream);

            var body = Encoding.UTF8.GetBytes(ResponseBody);
            var head = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(head, _stop.Token);
            await stream.WriteAsync(body, _stop.Token);
            await stream.FlushAsync(_stop.Token);
        }
        catch (Exception e) when (e is IOException or System.Security.Authentication.AuthenticationException or OperationCanceledException or ObjectDisposedException)
        {
            // A client that rejects the certificate drops the handshake; that is the point of these servers.
        }
    }

    private async Task ReadRequestHeadAsync(Stream stream)
    {
        var received = new StringBuilder();
        var buffer = new byte[1024];
        while (!received.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
        {
            var read = await stream.ReadAsync(buffer, _stop.Token);
            if (read == 0)
                return;
            received.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        _certificate.Dispose();
    }
}
