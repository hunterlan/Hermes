using Hermes.Core.Http;
using Hermes.Core.Import;
using Hermes.Core.Models;

namespace Hermes.Core.Tests;

public class CurlTests
{
    [Fact]
    public void Parses_json_post_with_headers_and_continuations()
    {
        const string curl = """
            curl -X POST 'https://api.test/users?x=1' \
              -H 'Content-Type: application/json' \
              -H "Authorization: Bearer abc" \
              --data-raw '{"name":"it'\''s"}'
            """;
        var r = CurlParser.Parse(curl);

        Assert.Equal("POST", r.Method);
        Assert.Equal("https://api.test/users?x=1", r.Url);
        Assert.Equal("x", Assert.Single(r.QueryParams).Key);
        Assert.Equal(BodyMode.Raw, r.Body.Mode);
        Assert.Equal(RawLanguage.Json, r.Body.RawLanguage);
        Assert.Equal("{\"name\":\"it's\"}", r.Body.Raw);
        Assert.Contains(r.Headers, h => h.Key == "Authorization" && h.Value == "Bearer abc");
    }

    [Fact]
    public void Data_without_content_type_becomes_urlencoded_post()
    {
        var r = CurlParser.Parse("curl https://x.test/login -d 'user=a%40b&pass=1'");
        Assert.Equal("POST", r.Method);
        Assert.Equal(BodyMode.UrlEncoded, r.Body.Mode);
        Assert.Equal("a@b", r.Body.UrlEncoded[0].Value);
    }

    [Fact]
    public void Parses_form_basic_auth_and_flags()
    {
        var r = CurlParser.Parse("curl -k -u admin:secret -F 'file=@/tmp/a.png' -F name=x --url https://x.test/up");
        Assert.Equal("POST", r.Method);
        Assert.Equal(AuthType.Basic, r.Auth.Type);
        Assert.Equal("secret", r.Auth.Basic.Password);
        Assert.False(r.Settings.VerifySsl);
        Assert.Equal(KeyValueKind.File, r.Body.FormData[0].Kind);
        Assert.Equal("/tmp/a.png", r.Body.FormData[0].Value);
        Assert.Equal("https://x.test/up", r.Url);
    }

    [Fact]
    public void Attached_short_flags()
    {
        var r = CurlParser.Parse("curl -XDELETE -H'X: 1' http://x.test");
        Assert.Equal("DELETE", r.Method);
        Assert.Equal("1", r.Headers.Single(h => h.Key == "X").Value);
    }

    [Fact]
    public void Generated_curl_parses_back_to_equivalent_request()
    {
        var original = new HttpRequestModel
        {
            Method = "PATCH",
            Url = "https://api.test/a?b=1",
            Headers = [new KeyValueItem("X-Quote", "it's")],
            Body = { Mode = BodyMode.Raw, Raw = "{\"a\":\"it's\"}", RawLanguage = RawLanguage.Json },
        };
        var curl = CurlGenerator.Generate(RequestPreparer.Prepare(original, RequestContext.Empty, includeDefaultHeaders: false));
        var parsed = CurlParser.Parse(curl);

        Assert.Equal("PATCH", parsed.Method);
        Assert.Equal(original.Url, parsed.Url);
        Assert.Equal(original.Body.Raw, parsed.Body.Raw);
        Assert.Contains(parsed.Headers, h => h.Key == "X-Quote" && h.Value == "it's");
    }
}
