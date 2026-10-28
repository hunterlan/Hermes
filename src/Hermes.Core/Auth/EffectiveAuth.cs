using Hermes.Core.Models.Auth;

namespace Hermes.Core.Auth;

public readonly record struct EffectiveAuth(AuthConfig Auth, string? InheritedFrom);
