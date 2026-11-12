using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Hermes.App.Services;

/// <summary>Shows a file or folder in the system file manager.</summary>
public static class FileRevealer
{
    /// <summary>
    /// Opens the file manager with <paramref name="path"/> selected (macOS, Windows) or its folder open (elsewhere).
    /// A folder is opened itself. Does nothing when the path and its folder are gone.
    /// </summary>
    public static async Task RevealAsync(TopLevel owner, string path)
    {
        if (Directory.Exists(path))
        {
            await owner.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
            return;
        }
        if (File.Exists(path) && TrySelectInFileManager(path))
            return;
        if (Path.GetDirectoryName(path) is { Length: > 0 } folder && Directory.Exists(folder))
            await owner.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(folder));
    }

    private static bool TrySelectInFileManager(string path)
    {
        ProcessStartInfo? start = null;
        if (OperatingSystem.IsMacOS())
            start = new ProcessStartInfo("open") { ArgumentList = { "-R", path } };
        else if (OperatingSystem.IsWindows())
            start = new ProcessStartInfo("explorer.exe") { Arguments = $"/select,\"{path}\"" };
        if (start is null)
            return false;
        try
        {
            start.UseShellExecute = false;
            Process.Start(start)?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
