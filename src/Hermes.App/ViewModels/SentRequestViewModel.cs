using CommunityToolkit.Mvvm.ComponentModel;
using Hermes.App.Services;
using Hermes.Core.Headers;
using Hermes.Core.Http;

namespace Hermes.App.ViewModels;

/// <summary>The request as it went out, for the "Sent request" tab: its line, headers (sensitive ones masked) and body.</summary>
public sealed partial class SentRequestViewModel : ObservableObject
{
    private const string SentSection = "sent";

    private readonly HeaderRevealState _revealState;

    public SentRequestViewModel(DialogService dialogs) => _revealState = new HeaderRevealState(dialogs.CopyTextAsync);

    /// <summary>The first line of the request that went out, e.g. "GET /users HTTP/1.1".</summary>
    [ObservableProperty] public partial string RequestLine { get; set; } = "";

    /// <summary>The headers that went out, sensitive ones masked until revealed.</summary>
    [ObservableProperty] public partial List<HeaderRowViewModel> HeaderRows { get; set; } = [];

    /// <summary>The body that went out; empty for none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBody))]
    public partial string BodyText { get; set; } = "";

    public bool HasBody => BodyText.Length > 0;

    /// <summary>Shows the request as it goes out, with its real values so they can be revealed and copied.</summary>
    public void SetSentRequest(PreparedRequest request)
    {
        // The text gives the request line and the body, in the same form as the wire text and the saved copy.
        var parsed = RawRequestParser.Parse(RawRequestFormatter.Format(request));
        var rows = new List<HeaderRowViewModel>();
        // Host is added when the request has none of its own, as the formatter does.
        if (request.GetHeader("Host") is null && parsed.Headers.FirstOrDefault(header => header.Name == "Host") is { } host)
            rows.Add(CreateSentRow(host.Name, host.Value, isAuthHeader: false));
        rows.AddRange(request.Headers.Select(header =>
            CreateSentRow(header.Key, header.Value, request.OriginOf(header) == PreparedHeaderOrigin.Auth)));
        ShowSentRequest(parsed.RequestLine, rows, parsed.Body);
    }

    /// <summary>Shows a saved request. It is only text, already masked where it was saved: its rows show that text.</summary>
    public void SetSavedRequest(string savedRequestText)
    {
        var parsed = RawRequestParser.Parse(savedRequestText);
        ShowSentRequest(parsed.RequestLine, parsed.Headers.Select(header =>
            CreateSentRow(header.Name, header.Value, isAuthHeader: false)), parsed.Body);
    }

    /// <summary>Masks the revealed values again, for when the user leaves the tab.</summary>
    public void HideRevealedValues()
    {
        _revealState.Clear();
        foreach (var row in HeaderRows)
            row.Sensitive.Hide();
    }

    private HeaderRowViewModel CreateSentRow(string name, string value, bool isAuthHeader) => new(name, value, "")
    {
        // Auth headers are credentials whatever their name (an API key may use any header).
        Sensitive = _revealState.CreateValue(SentSection, name, value, isAuthHeader || SensitiveHeaders.IsSensitiveName(name)),
    };

    private void ShowSentRequest(string requestLine, IEnumerable<HeaderRowViewModel> headerRows, string body)
    {
        RequestLine = requestLine;
        HeaderRows = headerRows.ToList();
        BodyText = body;
    }
}
