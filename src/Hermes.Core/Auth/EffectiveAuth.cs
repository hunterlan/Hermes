using Hermes.Core.Models;

namespace Hermes.Core.Auth;

public readonly record struct EffectiveAuth(AuthConfig Auth, string? InheritedFrom);
