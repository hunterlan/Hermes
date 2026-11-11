namespace Hermes.App.Tests;

/// <summary>A request rename that a <see cref="NullTabHost"/> was told about.</summary>
internal sealed record RenamedRequest(Guid RequestId, string NewName);
