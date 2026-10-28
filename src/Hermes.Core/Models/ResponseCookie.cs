namespace Hermes.Core.Models;

public sealed class ResponseCookie
{
    public string Name { get; init; } = "";
    public string Value { get; init; } = "";
    public string Domain { get; init; } = "";
    public string Path { get; init; } = "";
    public DateTime? Expires { get; init; }
    public bool HttpOnly { get; init; }
    public bool Secure { get; init; }
}
