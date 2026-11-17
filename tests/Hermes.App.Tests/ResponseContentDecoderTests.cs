using System.Text;
using Avalonia.Headless.XUnit;
using Hermes.App.ViewModels;
using Hermes.Core.Models;

namespace Hermes.App.Tests;

public class ResponseContentDecoderTests
{
    [Theory]
    [InlineData("application/json", "", "json")]
    [InlineData("text/html; charset=utf-8", "", "html")]
    [InlineData("application/atom+xml", "", "xml")]
    [InlineData("text/plain", "  {\"a\": 1}", "json")]
    [InlineData("text/plain", "<!doctype html><p>", "html")]
    [InlineData("text/plain", "<root/>", "xml")]
    [InlineData("text/plain", "hello", "text")]
    public void The_language_comes_from_the_content_type_then_the_body(string contentType, string body, string expected) =>
        Assert.Equal(expected, ResponseContentDecoder.DetectLanguage(contentType, body));

    [Fact]
    public void Json_is_prettified_and_text_is_left_alone()
    {
        Assert.Contains('\n', ResponseContentDecoder.Prettify("{\"a\":1}", "json"));
        Assert.Equal("{broken", ResponseContentDecoder.Prettify("{broken", "json"));
        Assert.Equal("plain", ResponseContentDecoder.Prettify("plain", "text"));
    }

    [Fact]
    public void A_body_is_decoded_in_the_charset_the_response_names()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var latin1 = new ResponseModel { Charset = "iso-8859-1", Body = [0x63, 0x61, 0x66, 0xE9] };

        Assert.Equal("café", ResponseContentDecoder.Decode(latin1));
    }

    [Fact]
    public void An_unknown_charset_falls_back_to_utf8()
    {
        var response = new ResponseModel { Charset = "no-such-charset", Body = Encoding.UTF8.GetBytes("café") };

        Assert.Equal("café", ResponseContentDecoder.Decode(response));
    }

    [AvaloniaFact]
    public void Bytes_that_are_not_an_image_give_null() =>
        Assert.Null(ResponseContentDecoder.TryDecodeImage("not an image"u8.ToArray()));
}
