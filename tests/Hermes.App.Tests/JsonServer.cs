using System.Text;
using Hermes.TestSupport;

namespace Hermes.App.Tests;

/// <summary>Minimal local HTTP server returning a JSON body.</summary>
internal sealed class JsonServer : IDisposable
{
    private readonly LocalHttpServer _server = new(async (ctx, stop) =>
    {
        var body = Encoding.UTF8.GetBytes(
            $"{{\"method\":\"{ctx.Request.HttpMethod}\",\"path\":\"{ctx.Request.Url!.PathAndQuery}\",\"items\":[1,2,3],\"nested\":{{\"ok\":true}}}}");
        ctx.Response.ContentType = "application/json";
        ctx.Response.AppendHeader("Set-Cookie", "session=abc123; Path=/");
        await ctx.Response.OutputStream.WriteAsync(body, stop);
        ctx.Response.Close();
    });

    public string Url => _server.Url;

    public void Dispose() => _server.Dispose();
}
