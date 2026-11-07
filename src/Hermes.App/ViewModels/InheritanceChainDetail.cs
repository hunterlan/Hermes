namespace Hermes.App.ViewModels;

/// <summary>One entry of an <see cref="InheritanceChainNode"/>'s detail line; struck through when something further in overrides it.</summary>
public sealed record InheritanceChainDetail(string Text, bool IsStruckThrough = false);
