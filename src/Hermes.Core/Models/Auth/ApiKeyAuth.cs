namespace Hermes.Core.Models.Auth;

public sealed class ApiKeyAuth
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public ApiKeyLocation AddTo { get; set; } = ApiKeyLocation.Header;
}
