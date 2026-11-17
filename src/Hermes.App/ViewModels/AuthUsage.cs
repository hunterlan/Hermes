namespace Hermes.App.ViewModels;

/// <summary>How a container's auth is used by the items under it.</summary>
/// <param name="Overrides">Items that set their own auth.</param>
/// <param name="Inheriting">How many requests end up using the container's auth.</param>
/// <param name="Total">How many requests are under the container.</param>
public sealed record AuthUsage(List<AuthOverride> Overrides, int Inheriting, int Total);
