namespace Hermes.App.ViewModels;

/// <summary>
/// Tells whether a request has unsaved edits by comparing its snapshot (the request serialized) with the one last
/// saved, and tells when the request is being loaded, so what changes then isn't taken for an edit.
/// </summary>
public sealed class RequestChangeTracker
{
    private string _savedSnapshot = "";
    private int _openLoadingScopes;

    /// <summary>True inside a <see cref="Loading"/> scope: the fields are being filled, so a change isn't an edit.</summary>
    public bool IsLoading => _openLoadingScopes > 0;

    /// <summary>Starts loading the request; it ends when the result is disposed. Scopes can be nested.</summary>
    public IDisposable Loading()
    {
        _openLoadingScopes++;
        return new LoadingScope(this);
    }

    /// <summary>Remembers <paramref name="snapshot"/> as the request as it is saved.</summary>
    public void MarkSaved(string snapshot) => _savedSnapshot = snapshot;

    /// <summary>Whether a request with <paramref name="currentSnapshot"/> differs from the saved one.</summary>
    public bool IsDirty(string currentSnapshot) => currentSnapshot != _savedSnapshot;

    private sealed class LoadingScope(RequestChangeTracker tracker) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            tracker._openLoadingScopes--;
        }
    }
}
