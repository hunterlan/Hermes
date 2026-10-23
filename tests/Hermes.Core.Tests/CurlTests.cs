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

    [Fact]
    public void Generated_curl_keeps_the_method_when_the_body_is_empty()
    {
        foreach (var mode in new[] { BodyMode.Raw, BodyMode.UrlEncoded, BodyMode.FormData })
        {
            var request = new HttpRequestModel { Method = "POST", Url = "https://api.test/x", Body = { Mode = mode } };

            var curl = CurlGenerator.Generate(RequestPreparer.Prepare(request, RequestContext.Empty, includeDefaultHeaders: false));

            Assert.Contains("--request POST", curl);
            Assert.Equal("POST", CurlParser.Parse(curl).Method);
        }
    }

    [Theory]
    [InlineData("curl --oauth2-bearer TOKEN https://api.test/x")]
    [InlineData("curl -E cert.pem https://api.test/x")]
    [InlineData("curl --aws-sigv4 aws:amz:eu-west-1:s3 https://api.test/x")]
    [InlineData("curl -Y 1 https://api.test/x")]
    [InlineData("curl --some-future-option value https://api.test/x")]
    public void Option_values_are_not_taken_for_the_url(string command) =>
        Assert.Equal("https://api.test/x", CurlParser.Parse(command).Url);

    [Fact]
    public void Json_option_posts_json_with_its_headers()
    {
        var r = CurlParser.Parse("curl --json '{\"a\":1}' https://api.test/x");

        Assert.Equal("https://api.test/x", r.Url);
        Assert.Equal("POST", r.Method);
        Assert.Equal(BodyMode.Raw, r.Body.Mode);
        Assert.Equal(RawLanguage.Json, r.Body.RawLanguage);
        Assert.Equal("{\"a\":1}", r.Body.Raw);
        Assert.Contains(r.Headers, h => h.Key == "Accept" && h.Value == "application/json");
    }

    [Fact]
    public void Combined_short_flags_are_all_read()
    {
        var r = CurlParser.Parse("curl -sSLk https://api.test/x");
        Assert.True(r.Settings.FollowRedirects);
        Assert.False(r.Settings.VerifySsl);

        var post = CurlParser.Parse("curl -sXPOST https://api.test/x");
        Assert.Equal("POST", post.Method);
        Assert.Equal("https://api.test/x", post.Url);
    }

    [Fact]
    public void Data_from_a_file_is_sent_from_that_file()
    {
        var r = CurlParser.Parse("curl -d @payload.json -H 'Content-Type: application/json' https://api.test/x");

        Assert.Equal(BodyMode.Binary, r.Body.Mode);
        Assert.Equal("payload.json", r.Body.BinaryFilePath);
        Assert.Equal("POST", r.Method);

        Assert.Equal("@payload.json", CurlParser.Parse("curl --data-raw @payload.json -H 'Content-Type: text/plain' https://api.test/x").Body.Raw);
    }

    [Fact]
    public void Bearer_option_becomes_bearer_auth()
    {
        var r = CurlParser.Parse("curl --oauth2-bearer abc https://api.test/x");
        Assert.Equal(AuthType.Bearer, r.Auth.Type);
        Assert.Equal("abc", r.Auth.Bearer.Token);
    }
}
