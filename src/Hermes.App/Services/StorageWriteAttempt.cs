using Hermes.Core.Storage;

namespace Hermes.App.Services;

/// <summary>One try at a write that can be offered again: completes with the error, or null when it worked.</summary>
public delegate Task<StorageError?> StorageWriteAttempt();
