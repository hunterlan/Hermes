using Hermes.Core.Http;

namespace Hermes.Core.Tests;

public class RawRequestParserTests
{
    [Fact]
    public void Splits_the_request_line_the_headers_and_the_body()
    {
        var parsed = RawRequestParser.Parse("POST /users?x=1 HTTP/1.1\nHost: api.test\nContent-Type: application/json\n\n{\"a\": \"b: c\"}\n\nend");

        Assert.Equal("POST /users?x=1 HTTP/1.1", parsed.RequestLine);
        Assert.Equal([new RawRequestHeader("Host", "api.test"), new RawRequestHeader("Content-Type", "application/json")], parsed.Headers);
        Assert.Equal("{\"a\": \"b: c\"}\n\nend", parsed.Body);
    }

    [Fact]
    public void A_request_without_a_body_has_an_empty_one()
    {
        var parsed = RawRequestParser.Parse("GET / HTTP/1.1\nHost: api.test\nAuthorization: Bearer ••••••\n");

        Assert.Equal(["Host", "Authorization"], parsed.Headers.Select(h => h.Name));
        Assert.Equal("Bearer ••••••", parsed.Headers[1].Value);
        Assert.Equal("", parsed.Body);
    }

    [Fact]
    public void Round_trips_what_the_formatter_writes()
    {
        var request = new PreparedRequest { Method = "PUT", Url = "https://api.test:8080/a?b=c", RawBody = "hello", BodyMode = Hermes.Core.Models.BodyMode.Raw };
        request.Headers.Add(new Hermes.Core.Models.KeyValueItem("X-One", "1"));

        var parsed = RawRequestParser.Parse(RawRequestFormatter.Format(request));

        Assert.Equal("PUT /a?b=c HTTP/1.1", parsed.RequestLine);
        Assert.Equal(["Host", "X-One"], parsed.Headers.Select(h => h.Name));
        Assert.Equal("api.test:8080", parsed.Headers[0].Value);
        Assert.Equal("hello", parsed.Body);
    }
}
