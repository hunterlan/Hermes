using Hermes.Core.Models;
using Hermes.Core.Storage;

namespace Hermes.App.Services;

/// <summary>The environments of the workspace, the global variables and which environment is active.</summary>
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

    /// <inheritdoc cref="WorkspaceService.CreateEnvironment"/>
    public EnvironmentModel Create(string name)
    {
        var environment = new EnvironmentModel { Name = name };
        Add(environment);
        return environment;
    }

    /// <inheritdoc cref="WorkspaceService.AddEnvironment"/>
    public bool Add(EnvironmentModel environment) => !_storage.Report(TryAdd(environment));

    /// <inheritdoc cref="WorkspaceService.TryAddEnvironment"/>
    public StorageError? TryAdd(EnvironmentModel environment)
    {
        var error = WorkspaceStorage.Attempt(() => _repository.SaveEnvironment(environment),
            _repository.EnvironmentFile(environment.Id), StorageSubjects.Environment(environment), StorageOperation.Write);
        if (error is not null)
            return error;
        Items.Add(environment);
        Items.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        EnvironmentsChanged?.Invoke();
        return null;
    }

    /// <inheritdoc cref="WorkspaceService.SaveEnvironment"/>
    public bool Save(EnvironmentModel environment) => !_storage.Report(TrySave(environment));

    /// <inheritdoc cref="WorkspaceService.TrySaveEnvironment"/>
    public StorageError? TrySave(EnvironmentModel environment)
    {
        var error = environment == Globals
            ? WorkspaceStorage.Attempt(() => _repository.SaveGlobals(environment), _repository.GlobalsFile,
                "Global variables", StorageOperation.Write)
            : WorkspaceStorage.Attempt(() => _repository.SaveEnvironment(environment),
                _repository.EnvironmentFile(environment.Id), StorageSubjects.Environment(environment), StorageOperation.Write);
        if (error is not null)
            return error;
        EnvironmentsChanged?.Invoke();
        VariablesChanged?.Invoke();
        return null;
    }

    /// <inheritdoc cref="WorkspaceService.DeleteEnvironment"/>
    public bool Delete(EnvironmentModel environment)
    {
        var error = WorkspaceStorage.Attempt(() => _repository.DeleteEnvironment(environment.Id),
            _repository.EnvironmentFile(environment.Id), StorageSubjects.Environment(environment), StorageOperation.Delete);
        if (_storage.Report(error))
            return false;
        Items.Remove(environment);
        if (Active == environment)
            Active = null;
        EnvironmentsChanged?.Invoke();
        return true;
    }
}
