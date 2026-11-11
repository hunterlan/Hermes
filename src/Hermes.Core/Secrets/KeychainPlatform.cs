namespace Hermes.Core.Secrets;

/// <summary>The operating system whose keychain Hermes uses, which decides how its failures are read.</summary>
public enum KeychainPlatform
{
    Other,
    MacOS,
    Linux,
    Windows,
}
