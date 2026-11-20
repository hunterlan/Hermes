using Avalonia.Headless.XUnit;
using Hermes.App.Services;
using Hermes.App.ViewModels;
using Hermes.Core.Models;
using Hermes.Core.Storage;

namespace Hermes.App.Tests;

/// <summary>The "Earlier response" tag shows only while an earlier saved response is on screen.</summary>
public class EarlierResponseTagTests
{
    private static SavedResponse SavedWith(int statusCode) =>
        new() { Response = new ResponseModel { StatusCode = statusCode, ReasonPhrase = "OK" } };

    private static ResponseModel LiveResponse() =>
        new() { StatusCode = 200, ReasonPhrase = "OK", ContentType = "text/plain", Body = "live"u8.ToArray() };

    /// <summary>A response pane with a latest and an earlier saved response, the earlier one picked.</summary>
    private static ResponseViewModel ShowingEarlier(TempWorkspace temp)
    {
        var response = new ResponseViewModel(new DialogService(), temp.Settings);
        response.Saved.SetSaved([SavedWith(200), SavedWith(201)], show: false);
        response.Saved.ShowSavedCommand.Execute(response.Saved.Items[1]);
        Assert.NotNull(response.Saved.ViewingEarlier);
        return response;
    }

    [AvaloniaFact]
    public void Picking_an_earlier_response_sets_the_tag_and_the_latest_does_not()
    {
        using var temp = new TempWorkspace();
        using var response = ShowingEarlier(temp);

        Assert.Same(response.Saved.Items[1], response.Saved.ViewingEarlier);
        Assert.Equal("201 OK", response.StatusText);

        response.Saved.ShowSavedCommand.Execute(response.Saved.Items[0]);

        Assert.Null(response.Saved.ViewingEarlier);
    }

    [AvaloniaFact]
    public void A_failure_clears_the_tag()
    {
        using var temp = new TempWorkspace();
        using var response = ShowingEarlier(temp);

        response.SetFailure(RequestFailure.FromMessage("Connection refused."));

        Assert.Null(response.Saved.ViewingEarlier);
    }

    [AvaloniaFact]
    public void A_live_response_that_is_not_saved_clears_the_tag()
    {
        using var temp = new TempWorkspace();
        using var response = ShowingEarlier(temp);

        response.SetResponse(LiveResponse());

        Assert.Null(response.Saved.ViewingEarlier);
    }

    [AvaloniaFact]
    public void A_cancelled_send_keeps_the_tag()
    {
        using var temp = new TempWorkspace();
        using var response = ShowingEarlier(temp);
        var earlier = response.Saved.ViewingEarlier;

        // The earlier response stays on screen (dimmed) while sending, and again after a cancel.
        response.BeginLoading();
        response.SetCancelled();

        Assert.Same(earlier, response.Saved.ViewingEarlier);
    }
}
