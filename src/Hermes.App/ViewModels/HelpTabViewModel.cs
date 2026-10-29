using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.App.Services;
using Hermes.Core.Models;

namespace Hermes.App.ViewModels;

/// <summary>The offline user manual, shown as a tab.</summary>
public sealed partial class HelpTabViewModel : TabViewModelBase
{
    /// <summary>The manual is a singleton tab.</summary>
    public static readonly Guid HelpId = new("2f7c1a64-8d3e-4b59-a0c2-5e9d6b41f873");

    private readonly ManualService _manual;

    public HelpTabViewModel(TabServices services, string? fileName = null) : base(services)
    {
        _manual = services.Manual;
        Title = "Manual";
        Navigate(fileName ?? ManualService.IndexFileName);
    }

    public override SessionTabKind Kind => SessionTabKind.Help;
    public override Guid EntityId => HelpId;
    public override string KindLabel => "HELP";

    public IReadOnlyList<ManualPage> Pages => _manual.Pages;

    [ObservableProperty] public partial string Markdown { get; set; } = "";
    [ObservableProperty] public partial ManualPage? SelectedPage { get; set; }

    public override Task<bool> SaveAsync() => Task.FromResult(true);

    /// <summary>Shows a manual page. Unknown pages are ignored.</summary>
    public void Navigate(string fileName)
    {
        if (!_manual.Exists(fileName))
            return;
        Markdown = _manual.Read(fileName);
        SelectedPage = Pages.FirstOrDefault(page => page.FileName == fileName);
    }

    /// <summary>Handles a link clicked in the manual: other pages open in the tab, web links in the browser.</summary>
    [RelayCommand]
    private void OpenLink(object? link)
    {
        var target = link?.ToString();
        if (string.IsNullOrEmpty(target))
            return;
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            _ = Services.Dialogs.OpenUrlAsync(uri);
        else
            Navigate(target.Split('#')[0]);
    }

    partial void OnSelectedPageChanged(ManualPage? value)
    {
        if (value is not null)
            Navigate(value.FileName);
    }
}
