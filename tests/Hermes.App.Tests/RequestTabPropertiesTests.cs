using Avalonia.Headless.XUnit;
using Hermes.App.ViewModels;
using Hermes.Core.Models;

namespace Hermes.App.Tests;

/// <summary>
/// The tab's own properties: the name and the method count as edits like the parts' fields do, and sending turns
/// the Send button into Cancel.
/// </summary>
public class RequestTabPropertiesTests
{
    private static RequestTabViewModel OpenTab(TempWorkspace temp) =>
        new(RequestTabTestServices.Create(temp), new HttpRequestModel { Id = Guid.NewGuid(), Name = "Probe" });

    [AvaloniaFact]
    public void Renaming_marks_the_tab_dirty_and_the_old_name_makes_it_clean_again()
    {
        using var temp = new TempWorkspace();
        using var tab = OpenTab(temp);
        Assert.False(tab.IsDirty);

        tab.Name = "Renamed";
        Assert.True(tab.IsDirty);
        Assert.Equal("Renamed", tab.Title);

        tab.Name = "Probe";
        Assert.False(tab.IsDirty);
        Assert.Equal("Probe", tab.Title);
    }

    [AvaloniaFact]
    public void Changing_the_method_marks_the_tab_dirty_and_the_old_method_makes_it_clean_again()
    {
        using var temp = new TempWorkspace();
        using var tab = OpenTab(temp);
        var announced = new List<string?>();
        tab.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

        tab.SelectedMethod = "POST";
        Assert.True(tab.IsDirty);
        Assert.Equal("POST", tab.Method);
        Assert.Equal("POST", tab.Sections.Method);
        Assert.Contains(nameof(RequestTabViewModel.Method), announced);

        tab.SelectedMethod = "GET";
        Assert.False(tab.IsDirty);
        Assert.Equal("GET", tab.Sections.Method);
    }

    [AvaloniaFact]
    public void Sending_switches_the_send_button_to_cancel()
    {
        using var temp = new TempWorkspace();
        using var tab = OpenTab(temp);
        var announced = new List<string?>();
        tab.Sending.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

        tab.Sending.IsSending = true;

        Assert.Equal("Cancel", tab.Sending.SendButtonText);
        Assert.Same(tab.Sending.CancelSendCommand, tab.Sending.SendButtonCommand);
        Assert.Contains(nameof(RequestSendViewModel.SendButtonText), announced);
        Assert.Contains(nameof(RequestSendViewModel.SendButtonCommand), announced);
    }
}
