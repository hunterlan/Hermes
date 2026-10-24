namespace Hermes.App.ViewModels;

public sealed record QuickLookSection(IReadOnlyList<QuickLookVariable> Rows)
{
    public bool IsEmpty => Rows.Count == 0;
}
