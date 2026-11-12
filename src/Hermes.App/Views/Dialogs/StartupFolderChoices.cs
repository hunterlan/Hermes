using Hermes.Core.Storage;

namespace Hermes.App.Views.Dialogs;

/// <summary>The buttons offered when the data folder can't be used at startup.</summary>
public static class StartupFolderChoices
{
    public const string UseDefaultFolder = "default-folder";
    public const string ChooseFolder = "choose-folder";

    /// <summary>
    /// "Use default folder" (only when <paramref name="failedFolder"/> isn't the default folder already, or it would
    /// fail the same way again) and "Choose another folder".
    /// </summary>
    public static IReadOnlyList<StorageDialogChoice> For(string failedFolder)
    {
        var choices = new List<StorageDialogChoice>();
        if (!IsSameFolder(failedFolder, AppPaths.DefaultDataDirectory))
            choices.Add(new StorageDialogChoice("Use default folder", UseDefaultFolder));
        choices.Add(new StorageDialogChoice("Choose another folder", ChooseFolder, "accent", PicksFolder: true));
        return choices;
    }

    private static bool IsSameFolder(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
}
