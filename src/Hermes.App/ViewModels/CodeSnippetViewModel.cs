using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.App.Services;

namespace Hermes.App.ViewModels;

public sealed partial class CodeSnippetViewModel(string code, DialogService dialogs) : ObservableObject
{
    public string Code { get; } = code;

    [ObservableProperty] public partial string CopyText { get; set; } = "Copy";

    [RelayCommand]
    private async Task Copy()
    {
        await dialogs.CopyTextAsync(Code);
        CopyText = "Copied!";
    }
}
