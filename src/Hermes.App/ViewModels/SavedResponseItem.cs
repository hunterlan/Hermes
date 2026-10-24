using Hermes.Core.Models;

namespace Hermes.App.ViewModels;

/// <summary>Entry in the saved-responses menu.</summary>
public sealed record SavedResponseItem(SavedResponse Saved, bool IsLatest)
{
    public string Label =>
        $"{Saved.Response.StatusCode} {Saved.Response.ReasonPhrase} · {Saved.Timestamp.LocalDateTime:g}{(IsLatest ? " · latest" : "")}";
}
