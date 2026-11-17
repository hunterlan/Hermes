using Hermes.App.ViewModels;
using Hermes.Core.Models;
using Hermes.Core.Storage;

namespace Hermes.App.Tests;

public class SavedResponsesViewModelTests
{
    private static SavedResponse SavedWith(int statusCode) =>
        new() { Response = new ResponseModel { StatusCode = statusCode, ReasonPhrase = "OK" } };

    [Fact]
    public void The_label_counts_the_earlier_responses()
    {
        var saved = new SavedResponsesViewModel();
        Assert.Equal("", saved.Label);
        Assert.False(saved.HasItems);

        saved.SetSaved([SavedWith(200)], show: false);
        Assert.Equal("Saved", saved.Label);
        Assert.True(saved.HasItems);

        saved.SetSaved([SavedWith(200), SavedWith(201)], show: false);
        Assert.Equal("Saved · 1 earlier response", saved.Label);

        saved.SetSaved([SavedWith(200), SavedWith(201), SavedWith(202)], show: false);
        Assert.Equal("Saved · 2 earlier responses", saved.Label);
    }

    [Fact]
    public void Opening_the_list_shows_the_newest_response()
    {
        var saved = new SavedResponsesViewModel();
        var shown = new List<SavedResponse>();
        saved.ShowRequested += (_, response) => shown.Add(response);
        var newest = SavedWith(200);

        saved.SetSaved([newest, SavedWith(201)], show: true);

        Assert.Same(newest, Assert.Single(shown));
        Assert.Null(saved.ViewingEarlier);
    }

    [Fact]
    public void Picking_an_earlier_response_shows_it_and_marks_it_as_earlier()
    {
        var saved = new SavedResponsesViewModel();
        var shown = new List<SavedResponse>();
        saved.ShowRequested += (_, response) => shown.Add(response);
        saved.SetSaved([SavedWith(200), SavedWith(201)], show: false);

        saved.ShowSavedCommand.Execute(saved.Items[1]);

        Assert.Same(saved.Items[1].Saved, Assert.Single(shown));
        Assert.Same(saved.Items[1], saved.ViewingEarlier);

        saved.ShowSavedCommand.Execute(saved.Items[0]);

        Assert.Null(saved.ViewingEarlier);
    }

    [Fact]
    public void A_just_saved_response_goes_first_and_the_list_stays_within_its_limit()
    {
        var saved = new SavedResponsesViewModel();
        for (var i = 0; i < WorkspaceRepository.MaxSavedResponses + 2; i++)
            saved.AddSaved(SavedWith(200 + i));

        Assert.Equal(WorkspaceRepository.MaxSavedResponses, saved.Items.Count);
        Assert.Equal(200 + WorkspaceRepository.MaxSavedResponses + 1, saved.Items[0].Saved.Response.StatusCode);
        Assert.True(saved.Items[0].IsLatest);
        Assert.False(saved.Items[1].IsLatest);
    }
}
