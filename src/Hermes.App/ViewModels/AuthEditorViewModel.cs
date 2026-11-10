using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Core.Auth;
using Hermes.Core.Http;
using Hermes.Core.Models;
using Hermes.Core.Models.Auth;
using Hermes.Core.Variables;

namespace Hermes.App.ViewModels;

public sealed partial class AuthEditorViewModel : ObservableObject, IDisposable
{
    private static readonly Option<AuthType> InheritOption = new("Inherit auth from parent", AuthType.Inherit);

    internal static readonly Option<AuthType>[] BaseTypes =
    [
        new("No Auth", AuthType.None),
        new("Basic Auth", AuthType.Basic),
        new("Bearer Token", AuthType.Bearer),
        new("API Key", AuthType.ApiKey),
        new("OAuth 2.0", AuthType.OAuth2),
    ];

    private readonly OAuth2Service _oauth;
    private readonly Func<Uri, Task<bool>> _openBrowser;
    private readonly Func<string, Task> _copyText;
    private bool _loading;
    private CancellationTokenSource? _tokenCts;
    private string? _refreshToken;
    private readonly TimeProvider _timeProvider;
    private DateTimeOffset? _expiresAt;

    /// <param name="openBrowser">Opens a URL in the system browser; false when none could be launched.</param>
    public AuthEditorViewModel(OAuth2Service oauth, Func<Uri, Task<bool>> openBrowser, Func<string, Task> copyText, bool allowInherit,
        TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _oauth = oauth;
        _openBrowser = openBrowser;
        _copyText = copyText;
        Types = allowInherit ? [InheritOption, .. BaseTypes] : BaseTypes;
        SelectedType = allowInherit ? InheritOption : BaseTypes[0];
        SelectedApiKeyLocation = ApiKeyLocations[0];
        SelectedGrantType = GrantTypes[0];
        SelectedClientAuth = ClientAuthOptions[0];
    }

    public IReadOnlyList<Option<AuthType>> Types { get; }

    public IReadOnlyList<Option<ApiKeyLocation>> ApiKeyLocations { get; } =
        [new("Header", ApiKeyLocation.Header), new("Query Params", ApiKeyLocation.Query)];

    public IReadOnlyList<Option<OAuth2GrantType>> GrantTypes { get; } =
    [
        new("Authorization Code", OAuth2GrantType.AuthorizationCode),
        new("Client Credentials", OAuth2GrantType.ClientCredentials),
        new("Password Credentials", OAuth2GrantType.Password),
    ];

    public IReadOnlyList<Option<OAuth2ClientAuthentication>> ClientAuthOptions { get; } =
    [
        new("Send as Basic Auth header", OAuth2ClientAuthentication.Header),
        new("Send client credentials in body", OAuth2ClientAuthentication.Body),
    ];

    /// <summary>Supplies the variables in effect, for resolving OAuth fields when fetching a token.</summary>
    public Func<VariableScope>? ScopeProvider { get; set; }

    /// <summary>Whether token requests verify SSL certificates: the setting in effect where the auth is edited.</summary>
    public Func<bool> VerifySslProvider { get; set; } = () => true;

    /// <summary>Where the setting from <see cref="VerifySslProvider"/> comes from: the request's own override, or the app-wide setting.</summary>
    public Func<SslSettingSource> SslSourceProvider { get; set; } = () => SslSettingSource.AppWide;

    /// <summary>
    /// Raised for a next step this editor can't carry out itself: opening the request's settings or the app's. The tab
    /// that owns the editor handles it, in the same place as the next steps of a response.
    /// </summary>
    public event EventHandler<RequestFailureActionEventArgs>? FailureActionRequested;

    /// <summary>The view should put the cursor in this field, e.g. the callback URL when its port is taken.</summary>
    public event Action<AuthEditorField>? FocusRequested;

    /// <summary>Asks the view to put the cursor in <paramref name="field"/>.</summary>
    public void RequestFocus(AuthEditorField field) => FocusRequested?.Invoke(field);

    /// <summary>Supplies the effective auth when "inherit" is selected, for the explanatory text.</summary>
    public Func<EffectiveAuth>? InheritedAuthProvider { get; set; }

    /// <summary>Supplies the levels above the edited item (outermost first) plus its own kind and name, for "where does auth come from".</summary>
    public Func<(IReadOnlyList<(string Kind, string Name, AuthConfig Auth)> Outer, string Kind, string Name)>? ChainProvider { get; set; }

    /// <summary>For collections and folders: the items whose auth this one supplies.</summary>
    public Func<IEnumerable<CollectionNode>>? ContainedItemsProvider { get; set; }

    public IReadOnlyList<InheritanceChainNode> Chain =>
        ChainProvider?.Invoke() is var (outer, kind, name) ? AuthInheritance.Chain(outer, (kind, name, ToModel())) : [];

    public IReadOnlyList<AuthOverride> Overrides =>
        ContainedItemsProvider is { } items ? AuthInheritance.Usage(items()).Overrides : [];

    public bool HasOverrides => Overrides.Count > 0;

    public string? UsageSummary
    {
        get
        {
            if (ContainedItemsProvider is not { } items)
                return null;
            var (overrides, inheriting, _) = AuthInheritance.Usage(items());
            var used = inheriting == 1 ? "Used by 1 request" : $"Used by {inheriting} requests";
            return overrides.Count == 0 ? $"{used}." : $"{used}. {overrides.Count} {(overrides.Count == 1 ? "overrides" : "override")}.";
        }
    }

    public event Action? Changed;

    [ObservableProperty] public partial Option<AuthType> SelectedType { get; set; }

    [ObservableProperty] public partial string BasicUsername { get; set; } = "";
    [ObservableProperty] public partial string BasicPassword { get; set; } = "";
    [ObservableProperty] public partial string BearerToken { get; set; } = "";
    [ObservableProperty] public partial string ApiKeyKey { get; set; } = "";
    [ObservableProperty] public partial string ApiKeyValue { get; set; } = "";
    [ObservableProperty] public partial Option<ApiKeyLocation> SelectedApiKeyLocation { get; set; }

    [ObservableProperty] public partial Option<OAuth2GrantType> SelectedGrantType { get; set; }
    [ObservableProperty] public partial string OAuthAuthUrl { get; set; } = "";
    [ObservableProperty] public partial string OAuthTokenUrl { get; set; } = "";
    [ObservableProperty] public partial string OAuthClientId { get; set; } = "";
    [ObservableProperty] public partial string OAuthClientSecret { get; set; } = "";
    [ObservableProperty] public partial string OAuthScope { get; set; } = "";
    [ObservableProperty] public partial string OAuthRedirectUri { get; set; } = "";
    [ObservableProperty] public partial string OAuthState { get; set; } = "";
    [ObservableProperty] public partial bool OAuthUsePkce { get; set; } = true;
    [ObservableProperty] public partial string OAuthUsername { get; set; } = "";
    [ObservableProperty] public partial string OAuthPassword { get; set; } = "";
    [ObservableProperty] public partial Option<OAuth2ClientAuthentication> SelectedClientAuth { get; set; }
    [ObservableProperty] public partial string OAuthHeaderPrefix { get; set; } = "Bearer";
    [ObservableProperty] public partial string OAuthAccessToken { get; set; } = "";

    /// <summary>Masks <see cref="BearerToken"/> in the form until revealed or focused.</summary>
    public SensitiveValueViewModel BearerTokenSecret { get; } = CreateSecret();

    /// <summary>Masks <see cref="ApiKeyValue"/> in the form until revealed or focused.</summary>
    public SensitiveValueViewModel ApiKeyValueSecret { get; } = CreateSecret();

    /// <summary>Masks <see cref="OAuthAccessToken"/> in the form until revealed or focused.</summary>
    public SensitiveValueViewModel AccessTokenSecret { get; } = CreateSecret();

    [ObservableProperty] public partial string TokenStatus { get; set; } = "";

    /// <summary>The sign-in address, shown while waiting for authorization when no browser could be opened.</summary>
    [ObservableProperty] public partial string? AuthorizationUrl { get; set; }

    [ObservableProperty] public partial bool IsFetchingToken { get; set; }

    /// <summary>Why the last attempt to get a token failed; null while there is no failure to show.</summary>
    [ObservableProperty] public partial RequestFailureViewModel? Failure { get; set; }

    /// <summary>The label of the one next step the failure offers.</summary>
    public string FailureActionLabel => Failure?.Model.Actions.FirstOrDefault() switch
    {
        RequestFailureAction.Retry => "Try again",
        RequestFailureAction.EditUrl => "Edit token URL",
        RequestFailureAction.EditCallbackUrl => "Edit callback URL",
        RequestFailureAction.OpenRequestSettings => "Open request settings",
        RequestFailureAction.OpenAppSettings => "Open settings",
        _ => "",
    };

    public bool IsInherit => SelectedType.Value == AuthType.Inherit;
    public bool IsNone => SelectedType.Value == AuthType.None;
    public bool IsBasic => SelectedType.Value == AuthType.Basic;
    public bool IsBearer => SelectedType.Value == AuthType.Bearer;
    public bool IsApiKey => SelectedType.Value == AuthType.ApiKey;
    public bool IsOAuth2 => SelectedType.Value == AuthType.OAuth2;
    public bool IsAuthCodeGrant => SelectedGrantType.Value == OAuth2GrantType.AuthorizationCode;
    public bool IsPasswordGrant => SelectedGrantType.Value == OAuth2GrantType.Password;

    public string TypeDescription => SelectedType.Value switch
    {
        AuthType.Inherit => InheritedDescription,
        AuthType.None => "This request does not use any authorization.",
        AuthType.Basic => "The authorization header will be automatically generated when you send the request.",
        AuthType.Bearer => "The authorization header will be automatically generated when you send the request.",
        AuthType.ApiKey => "The key-value pair is added as a header or query param when you send the request.",
        AuthType.OAuth2 => "The access token is sent in the Authorization header. Use \"Get New Access Token\" to obtain one.",
        _ => "",
    };

    public string InheritedDescription
    {
        get
        {
            if (InheritedAuthProvider?.Invoke() is not { } effective)
                return "This request is using the authorization of its parent.";
            var source = effective.InheritedFrom is null ? "its parent" : $"\"{effective.InheritedFrom}\"";
            var label = BaseTypes.FirstOrDefault(t => t.Value == effective.Auth.Type)?.Label ?? "No Auth";
            return $"This request is using {label} from {source}.";
        }
    }

    public string TokenExpiry => _expiresAt is { } expires
        ? expires < _timeProvider.GetUtcNow() ? "Token expired" : $"Expires {expires.LocalDateTime:g}"
        : "";

    /// <summary>One line for the collapsed Auth section, e.g. "Inherited · OAuth 2.0 from Payments API".</summary>
    public string Summary
    {
        get
        {
            if (!IsInherit)
                return SelectedType.Label;
            if (InheritedAuthProvider?.Invoke() is not { } effective)
                return "Inherited";
            var label = BaseTypes.FirstOrDefault(t => t.Value == effective.Auth.Type)?.Label ?? "No Auth";
            return effective.InheritedFrom is null ? $"Inherited · {label}" : $"Inherited · {label} from {effective.InheritedFrom}";
        }
    }

    public void RefreshInherited()
    {
        OnPropertyChanged(nameof(TypeDescription));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Chain));
        OnPropertyChanged(nameof(Overrides));
        OnPropertyChanged(nameof(HasOverrides));
        OnPropertyChanged(nameof(UsageSummary));
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        switch (e.PropertyName)
        {
            case nameof(SelectedType):
                OnPropertyChanged(nameof(IsInherit));
                OnPropertyChanged(nameof(IsNone));
                OnPropertyChanged(nameof(IsBasic));
                OnPropertyChanged(nameof(IsBearer));
                OnPropertyChanged(nameof(IsApiKey));
                OnPropertyChanged(nameof(IsOAuth2));
                OnPropertyChanged(nameof(TypeDescription));
                OnPropertyChanged(nameof(Summary));
                OnPropertyChanged(nameof(Chain));
                // A token failure belongs to the OAuth settings it came from, not to whatever is picked next.
                Failure = null;
                break;
            case nameof(SelectedGrantType):
                OnPropertyChanged(nameof(IsAuthCodeGrant));
                OnPropertyChanged(nameof(IsPasswordGrant));
                Failure = null;
                break;
            case nameof(Failure):
                OnPropertyChanged(nameof(FailureActionLabel));
                break;
        }

        if (!_loading && e.PropertyName is not (nameof(TokenStatus) or nameof(Failure) or nameof(AuthorizationUrl) or nameof(IsFetchingToken) or nameof(TokenButtonCommand) or nameof(TypeDescription) or nameof(Summary) or nameof(Chain) or nameof(Overrides) or nameof(HasOverrides) or nameof(UsageSummary))
            && !e.PropertyName!.StartsWith("Is", StringComparison.Ordinal) && !e.PropertyName.StartsWith("Failure", StringComparison.Ordinal))
            Changed?.Invoke();
    }

    public void Load(AuthConfig auth)
    {
        _loading = true;
        SelectedType = Types.FirstOrDefault(t => t.Value == auth.Type) ?? Types[0];
        BasicUsername = auth.Basic.Username;
        BasicPassword = auth.Basic.Password;
        BearerToken = auth.Bearer.Token;
        ApiKeyKey = auth.ApiKey.Key;
        ApiKeyValue = auth.ApiKey.Value;
        SelectedApiKeyLocation = ApiKeyLocations.First(l => l.Value == auth.ApiKey.AddTo);
        var o = auth.OAuth2;
        SelectedGrantType = GrantTypes.First(g => g.Value == o.GrantType);
        OAuthAuthUrl = o.AuthUrl;
        OAuthTokenUrl = o.AccessTokenUrl;
        OAuthClientId = o.ClientId;
        OAuthClientSecret = o.ClientSecret;
        OAuthScope = o.Scope;
        OAuthRedirectUri = o.RedirectUri;
        OAuthState = o.State;
        OAuthUsePkce = o.UsePkce;
        OAuthUsername = o.Username;
        OAuthPassword = o.Password;
        SelectedClientAuth = ClientAuthOptions.First(c => c.Value == o.ClientAuthentication);
        OAuthHeaderPrefix = o.HeaderPrefix;
        OAuthAccessToken = o.AccessToken;
        _refreshToken = o.RefreshToken;
        _expiresAt = o.ExpiresAt;
        TokenStatus = "";
        Failure = null;
        _loading = false;
        OnPropertyChanged(nameof(TokenExpiry));
    }

    public AuthConfig ToModel() => new()
    {
        Type = SelectedType.Value,
        Basic = new BasicAuth { Username = BasicUsername, Password = BasicPassword },
        Bearer = new BearerAuth { Token = BearerToken },
        ApiKey = new ApiKeyAuth { Key = ApiKeyKey, Value = ApiKeyValue, AddTo = SelectedApiKeyLocation.Value },
        OAuth2 = new OAuth2Auth
        {
            GrantType = SelectedGrantType.Value,
            AuthUrl = OAuthAuthUrl,
            AccessTokenUrl = OAuthTokenUrl,
            ClientId = OAuthClientId,
            ClientSecret = OAuthClientSecret,
            Scope = OAuthScope,
            RedirectUri = OAuthRedirectUri,
            State = OAuthState,
            UsePkce = OAuthUsePkce,
            Username = OAuthUsername,
            Password = OAuthPassword,
            ClientAuthentication = SelectedClientAuth.Value,
            HeaderPrefix = OAuthHeaderPrefix,
            AccessToken = OAuthAccessToken,
            RefreshToken = _refreshToken,
            ExpiresAt = _expiresAt,
        },
    };

    /// <summary>What the token button does: fetch a token, or cancel the fetch in progress.</summary>
    public ICommand TokenButtonCommand => IsFetchingToken ? CancelTokenCommand : GetNewAccessTokenCommand;

    partial void OnIsFetchingTokenChanged(bool value) => OnPropertyChanged(nameof(TokenButtonCommand));

    partial void OnBearerTokenChanged(string value) => UpdateSecret(BearerTokenSecret, value);

    partial void OnApiKeyValueChanged(string value) => UpdateSecret(ApiKeyValueSecret, value);

    partial void OnOAuthAccessTokenChanged(string value) => UpdateSecret(AccessTokenSecret, value);

    /// <summary>Masks every revealed credential again, for when the user leaves the tab.</summary>
    public void HideRevealedValues()
    {
        BearerTokenSecret.Hide();
        ApiKeyValueSecret.Hide();
        AccessTokenSecret.Hide();
    }

    private static SensitiveValueViewModel CreateSecret() => new("", isSensitive: false, copyText: null);

    /// <summary>
    /// A credential typed in is masked. One that only names variables (e.g. "{{token}}") shows no secret, so it stays
    /// readable.
    /// </summary>
    private static void UpdateSecret(SensitiveValueViewModel secret, string value)
    {
        secret.Value = value;
        secret.IsSensitive = !VariableResolver.IsOnlyVariables(value);
    }

    [RelayCommand]
    private void CancelToken() => _tokenCts?.Cancel();

    /// <summary>Cancels a token request in progress, which also stops the callback listener and frees its port.</summary>
    public void Dispose() => CancelToken();

    [RelayCommand]
    private async Task GetNewAccessToken()
    {
        IsFetchingToken = true;
        Failure = null;
        TokenStatus = IsAuthCodeGrant ? "Waiting for authorization in your browser…" : "Requesting token…";
        _tokenCts = new CancellationTokenSource();
        try
        {
            var scope = ScopeProvider?.Invoke() ?? VariableScope.Empty;
            var result = await _oauth.GetTokenAsync(ToModel().OAuth2, scope, OpenBrowserAsync, VerifySslProvider(), _tokenCts.Token, SslSourceProvider());
            if (result.IsSuccess)
            {
                _refreshToken = result.RefreshToken;
                _expiresAt = result.ExpiresAt;
                OAuthAccessToken = result.AccessToken!;
                TokenStatus = "Token received.";
                OnPropertyChanged(nameof(TokenExpiry));
            }
            else if (result.IsCancelled)
            {
                TokenStatus = "Cancelled.";
            }
            else
            {
                ShowFailure(result.Failure ?? OAuth2FailureDescriber.FromMessage("No token was received."));
            }
        }
        catch (Exception ex)
        {
            // A Hermes bug, or something nobody has met yet. Once logging exists (#92) this should be logged too.
            ShowFailure(OAuth2FailureDescriber.FromUnexpected(ex));
        }
        finally
        {
            IsFetchingToken = false;
            AuthorizationUrl = null;
            _tokenCts.Dispose();
            _tokenCts = null;
        }
    }

    private void ShowFailure(RequestFailure failure)
    {
        TokenStatus = "";
        Failure = new RequestFailureViewModel(failure, _copyText);
        // The one thing to change is the callback port, so the cursor goes there.
        if (failure.Kind == RequestFailureKind.CallbackPortInUse)
            FocusRequested?.Invoke(AuthEditorField.CallbackUrl);
    }

    [RelayCommand]
    private async Task RunFailureAction()
    {
        if (Failure is not { } failure)
            return;

        var action = failure.Model.Actions.FirstOrDefault();
        switch (action)
        {
            case RequestFailureAction.Retry:
                await GetNewAccessTokenCommand.ExecuteAsync(null);
                break;
            case RequestFailureAction.EditUrl:
                FocusRequested?.Invoke(AuthEditorField.AccessTokenUrl);
                break;
            case RequestFailureAction.EditCallbackUrl:
                FocusRequested?.Invoke(AuthEditorField.CallbackUrl);
                break;
            case RequestFailureAction.OpenRequestSettings:
            case RequestFailureAction.OpenAppSettings:
                FailureActionRequested?.Invoke(this, new RequestFailureActionEventArgs(action, failure.Model));
                break;
        }
    }

    // Without a browser the user can still sign in by opening the address themselves: the callback listener waits
    // either way. Runs on the UI thread, as OAuth2Service opens the browser before its first await.
    private async Task OpenBrowserAsync(Uri uri)
    {
        if (await _openBrowser(uri))
            return;
        AuthorizationUrl = uri.AbsoluteUri;
        TokenStatus = "Couldn't open a browser. Open this address to sign in:";
    }

    [RelayCommand]
    private Task CopyAuthorizationUrl() => AuthorizationUrl is { } url ? _copyText(url) : Task.CompletedTask;

    [RelayCommand]
    private void ClearToken()
    {
        OAuthAccessToken = "";
        _refreshToken = null;
        _expiresAt = null;
        TokenStatus = "";
        Failure = null;
        OnPropertyChanged(nameof(TokenExpiry));
    }
}
