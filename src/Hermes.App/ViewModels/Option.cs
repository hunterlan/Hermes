namespace Hermes.App.ViewModels;

/// <summary>A labelled choice for combo boxes / radio groups.</summary>
public sealed record Option<T>(string Label, T Value)
{
    public override string ToString() => Label;
}
