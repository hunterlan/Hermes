using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.Core.Http;
using Hermes.Core.Models;

namespace Hermes.App.Tests;

public class SentRequestViewModelTests
{
    private static PreparedRequest RequestWithBearerToken()
    {
        var request = new PreparedRequest { Method = "GET", Url = "https://example.test/items" };
        request.AddAutomaticHeader(new KeyValueItem("Authorization", "Bearer abc"), PreparedHeaderOrigin.Auth);
        return request;
    }

    [Fact]
    public void A_set_request_shows_its_line_headers_and_no_body()
    {
        var sent = new SentRequestViewModel(new DialogService());

        sent.SetSentRequest(RequestWithBearerToken());

        Assert.Equal("GET /items HTTP/1.1", sent.RequestLine);
        Assert.Equal(["Host", "Authorization"], sent.HeaderRows.Select(row => row.Key));
        Assert.True(sent.HeaderRows[1].Sensitive.IsSensitive);
        Assert.False(sent.HasBody);
    }

    [Fact]
    public void Hiding_masks_the_revealed_values_again_and_forgets_them_for_new_rows()
    {
        var sent = new SentRequestViewModel(new DialogService());
        sent.SetSentRequest(RequestWithBearerToken());
        sent.HeaderRows[1].Sensitive.IsRevealed = true;

        sent.HideRevealedValues();

        Assert.False(sent.HeaderRows[1].Sensitive.IsRevealed);
        sent.SetSentRequest(RequestWithBearerToken());
        Assert.False(sent.HeaderRows[1].Sensitive.IsRevealed);
    }

    [Fact]
    public void A_revealed_value_stays_revealed_when_the_request_is_set_again()
    {
        var sent = new SentRequestViewModel(new DialogService());
        sent.SetSentRequest(RequestWithBearerToken());
        sent.HeaderRows[1].Sensitive.IsRevealed = true;

        sent.SetSentRequest(RequestWithBearerToken());

        Assert.True(sent.HeaderRows[1].Sensitive.IsRevealed);
    }
}
