using Hermes.Core.Headers;
using Hermes.Core.Secrets;

namespace Hermes.Core.Tests;

public class SensitiveHeadersTests
{
    [Theory]
    [InlineData("Authorization")]
    [InlineData("authorization")]
    [InlineData("PROXY-AUTHORIZATION")]
    [InlineData("Cookie")]
    [InlineData("x-api-key")]
    [InlineData(" X-Api-Key ")]
    public void Credential_header_names_are_sensitive_ignoring_case(string name) =>
        Assert.True(SensitiveHeaders.IsSensitiveName(name));

    [Theory]
    [InlineData("Content-Type")]
    [InlineData("Set-Cookie")]
    [InlineData("")]
    [InlineData(null)]
    public void Other_header_names_are_not_sensitive(string? name) =>
        Assert.False(SensitiveHeaders.IsSensitiveName(name));

    [Fact]
    public void The_mask_is_dots_only()
    {
        Assert.All(SensitiveHeaders.MaskText, character => Assert.Equal('•', character));
    }

    [Fact]
    public void Saved_request_text_masks_cookie_and_api_key_headers()
    {
        var text = "GET / HTTP/1.1\nCookie: session=abc def\nX-Api-Key: key-123\nAuthorization: Bearer tok\n\nbody";

        var masked = SecretMasker.MaskCredentialHeaders(text);

        Assert.DoesNotContain("abc", masked);
        Assert.DoesNotContain("key-123", masked);
        Assert.Contains("Authorization: Bearer ", masked);
        Assert.DoesNotContain("tok\n", masked);
        Assert.EndsWith("\n\nbody", masked);
    }
}
