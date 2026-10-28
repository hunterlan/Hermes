namespace Hermes.App.ViewModels;

/// <summary>The first <see cref="MainWindowViewModel.QuickLookLimit"/> rows of a section; the rest live in the matrix.</summary>
public sealed record QuickLookSection(IReadOnlyList<QuickLookVariable> Rows, int Total)
{
    public bool IsEmpty => Rows.Count == 0;
    public int MoreCount => Total - Rows.Count;
    public bool HasMore => MoreCount > 0;
    public string MoreText => $"+{MoreCount} more in the matrix";
}
