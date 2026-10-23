using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Core.Auth;
using Hermes.Core.Http;
using Hermes.Core.Models;
using Hermes.Core.Variables;

namespace Hermes.App.ViewModels;

public sealed partial class AuthEditorViewModel : ObservableObject
{
    private readonly OAuth2Service _oauth;
    private readonly Func<Uri, Task> _openBrowser;
    private bool _loading;
    private CancellationTokenSource? _tokenCts;

    public AuthEditorViewModel(OAuth2Service oauth, Func<Uri, Task> openBrowser, bool allowInherit)
    {
        _oauth = oauth;
        _openBrowser = openBrowser;
        Types = allowInherit ? [InheritOption, .. BaseTypes] : BaseTypes;
        SelectedType = allowInherit ? InheritOption : BaseTypes[0];
        SelectedApiKeyLocation = ApiKeyLocations[0];
        SelectedGrantType = GrantTypes[0];
        SelectedClientAuth = ClientAuthOptions[0];
    }

    private static readonly Option<AuthType> InheritOption = new("Inherit auth from parent", AuthType.Inherit);

    internal static readonly Option<AuthType>[] BaseTypes =
    [
        new("No Auth", AuthType.None),
        new("Basic Auth", AuthType.Basic),
        new("Bearer Token", AuthType.Bearer),
        new("API Key", AuthType.ApiKey),
        new("OAuth 2.0", AuthType.OAuth2),
    ];

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

    /// <summary>Supplies the effective auth when "inherit" is selected, for the explanatory text.</summary>
    public Func<EffectiveAuth>? InheritedAuthProvider { get; set; }

    /// <summary>Supplies the levels above the edited item (outermost first) plus its own kind and name, for "where does auth come from".</summary>
    public Func<(IReadOnlyList<(string Kind, string Name, AuthConfig Auth)> Outer, string Kind, string Name)>? ChainProvider { get; set; }

    /// <summary>For collections and folders: the items whose auth this one supplies.</summary>
    public Func<IEnumerable<CollectionNode>>? ContainedItemsProvider { get; set; }

    public IReadOnlyList<AuthChainNode> Chain =>
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

    private string? _refreshToken;
    private DateTimeOffset? _expiresAt;

    [ObservableProperty] public partial string TokenStatus { get; set; } = "";
    [ObservableProperty] public partial bool IsFetchingToken { get; set; }

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
        ? expires < DateTimeOffset.Now ? "Token expired" : $"Expires {expires.LocalDateTime:g}"
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
                break;
            case nameof(SelectedGrantType):
                OnPropertyChanged(nameof(IsAuthCodeGrant));
                OnPropertyChanged(nameof(IsPasswordGrant));
                break;
        }

        if (!_loading && e.PropertyName is not (nameof(TokenStatus) or nameof(IsFetchingToken) or nameof(TypeDescription) or nameof(Summary) or nameof(Chain) or nameof(Overrides) or nameof(HasOverrides) or nameof(UsageSummary))
            && !e.PropertyName!.StartsWith("Is", StringComparison.Ordinal))
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

    [RelayCommand]
    private async Task GetNewAccessToken()
    {
        if (IsFetchingToken)
        {
            _tokenCts?.Cancel();
            return;
        }

        IsFetchingToken = true;
        TokenStatus = IsAuthCodeGrant ? "Waiting for authorization in your browser…" : "Requesting token…";
        _tokenCts = new CancellationTokenSource();
        try
        {
            var scope = ScopeProvider?.Invoke() ?? VariableScope.Empty;
            var result = await _oauth.GetTokenAsync(ToModel().OAuth2, scope, _openBrowser, verifySsl: true, _tokenCts.Token);
            if (result.IsSuccess)
            {
                _refreshToken = result.RefreshToken;
                _expiresAt = result.ExpiresAt;
                OAuthAccessToken = result.AccessToken!;
                TokenStatus = "Token received.";
                OnPropertyChanged(nameof(TokenExpiry));
            }
            else
            {
                TokenStatus = "Error: " + result.Error;
            }
        }
        catch (Exception ex)
        {
            TokenStatus = "Error: " + ex.Message;
        }
        finally
        {
            IsFetchingToken = false;
            _tokenCts.Dispose();
            _tokenCts = null;
        }
    }

    [RelayCommand]
    private void ClearToken()
    {
        OAuthAccessToken = "";
        _refreshToken = null;
        _expiresAt = null;
        TokenStatus = "";
        OnPropertyChanged(nameof(TokenExpiry));
    }
}
