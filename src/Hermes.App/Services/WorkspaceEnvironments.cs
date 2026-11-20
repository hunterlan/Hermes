using Hermes.Core.Models;
using Hermes.Core.Serialization;
using Hermes.Core.Storage;

namespace Hermes.App.Services;

/// <summary>
/// The environments of the workspace, the global variables and which environment is active. Like collections, an
/// environment is written off the UI thread from a copy taken when the write is asked for.
/// </summary>
internal sealed class WorkspaceEnvironments
{
    private readonly WorkspaceRepository _repository;
    private readonly WorkspaceStorage _storage;

    private EnvironmentModel? _active;

    public WorkspaceEnvironments(WorkspaceRepository repository, WorkspaceStorage storage)
    {
        _repository = repository;
        _storage = storage;
        Items = repository.LoadEnvironments();
        Globals = repository.LoadGlobals();
    }

    public List<EnvironmentModel> Items { get; }
    public EnvironmentModel Globals { get; }

    public EnvironmentModel? Active
    {
        get => _active;
        set
        {
            if (_active == value)
                return;
            _active = value;
            VariablesChanged?.Invoke();
        }
    }

    public event Action? EnvironmentsChanged;

    /// <summary>The active environment changed, or an environment or the globals were saved.</summary>
    public event Action? VariablesChanged;

    public EnvironmentModel? Find(Guid id) => id == Globals.Id ? Globals : Items.FirstOrDefault(e => e.Id == id);

    /// <inheritdoc cref="WorkspaceService.CreateEnvironmentAsync"/>
    public async Task<EnvironmentModel> CreateAsync(string name)
    {
        var environment = new EnvironmentModel { Name = name };
        await AddAsync(environment);
        return environment;
    }

    /// <inheritdoc cref="WorkspaceService.AddEnvironmentAsync"/>
    public async Task<bool> AddAsync(EnvironmentModel environment) => !_storage.Report(await TryAddAsync(environment));

    /// <inheritdoc cref="WorkspaceService.TryAddEnvironmentAsync"/>
    public async Task<StorageError?> TryAddAsync(EnvironmentModel environment)
    {
        if (await TrySaveFileAsync(environment) is { } error)
            return error;
        Items.Add(environment);
        Items.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        EnvironmentsChanged?.Invoke();
        return null;
    }

    /// <inheritdoc cref="WorkspaceService.SaveEnvironmentAsync"/>
    public async Task<bool> SaveAsync(EnvironmentModel environment) => !_storage.Report(await TrySaveAsync(environment));

    /// <inheritdoc cref="WorkspaceService.TrySaveEnvironmentAsync"/>
    public async Task<StorageError?> TrySaveAsync(EnvironmentModel environment)
    {
        var error = environment == Globals ? await TrySaveGlobalsAsync() : await TrySaveFileAsync(environment);
        if (error is not null)
            return error;
        EnvironmentsChanged?.Invoke();
        VariablesChanged?.Invoke();
        return null;
    }

    /// <inheritdoc cref="WorkspaceService.DeleteEnvironmentAsync"/>
    public async Task<bool> DeleteAsync(EnvironmentModel environment)
    {
        var id = environment.Id;
        var error = await _storage.AttemptAsync(() => _repository.DeleteEnvironment(id),
            _repository.EnvironmentFile(id), StorageSubjects.Environment(environment), StorageOperation.Delete);
        if (_storage.Report(error))
            return false;
        Items.Remove(environment);
        if (Active == environment)
            Active = null;
        EnvironmentsChanged?.Invoke();
        return true;
    }

    // The copies are taken here, on the UI thread; the queue writes them later.
    private Task<StorageError?> TrySaveFileAsync(EnvironmentModel environment)
    {
        var snapshot = ModelCloner.Clone(environment);
        return _storage.AttemptAsync(() => _repository.SaveEnvironment(snapshot), _repository.EnvironmentFile(environment.Id),
            StorageSubjects.Environment(environment), StorageOperation.Write);
    }

    private Task<StorageError?> TrySaveGlobalsAsync()
    {
        var snapshot = ModelCloner.Clone(Globals);
        return _storage.AttemptAsync(() => _repository.SaveGlobals(snapshot), _repository.GlobalsFile,
            "Global variables", StorageOperation.Write);
    }
}
