using Odysseum.Abstractions.Projects;
using Odysseum.Server.Models;
using Odysseum.Server.Repositories;
using ProjectSettings = Odysseum.Server.Settings.ProjectSettings;

namespace Odysseum.Server.Services.Projects;

/// <summary>One open project branch. It holds the current <see cref="Project"/> and runs every operation under one
/// lock. An operation reloads the project first, so it always works on what is stored now.</summary>
public sealed class ProjectSession : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IProjectRepository _repository;
    private IAsyncDisposable? _lease;

    internal ProjectSession(ProjectBranch branch, IProjectRepository repository, ProjectEvents events)
    {
        Branch = branch;
        _repository = repository;
        Events = events;
        Current = new Project(branch, new ProjectData("", new ProjectSettings { Title = branch.Project }, "", new Folder(Guid.Empty.ToString(), branch.Project), null));
    }

    public ProjectBranch Branch { get; }
    public string Name => Branch.Project;
    public Project Current { get; private set; }
    public ProjectEvents Events { get; }
    public event Action<Project>? Changed;
    public event Action<IReadOnlyList<Document>>? DocumentsRemoved;
    public event Action<IReadOnlyList<Folder>>? FoldersRemoved;

    internal async Task OpenAsync()
    {
        _lease = await _repository.OpenAsync(Branch);
        await ReloadAsync();
    }

    /// <summary>Runs one operation under the lock. The project is reloaded first unless <c>reload</c> is false.</summary>
    public async Task<T> RunAsync<T>(Func<Task<T>> operation, bool reload = true)
    {
        await _gate.WaitAsync();
        try
        {
            if (reload) await RefreshAsync();
            return await operation();
        }
        finally { _gate.Release(); }
    }

    /// <summary>Reloads the project under the lock and returns it.</summary>
    public Task<Project> ReloadAsync() => RunAsync(() => Task.FromResult(Current));

    /// <summary>Reloads the project. Call it only inside <see cref="RunAsync{T}"/>.</summary>
    public async Task<Project> RefreshAsync()
    {
        Apply(new Project(Branch, await _repository.LoadAsync(Branch)));
        return Current;
    }

    /// <summary>Saves the changes and makes the saved state current. Call it only inside <see cref="RunAsync{T}"/>.</summary>
    public async Task<Project> SaveAsync(Changes changes, string revision)
    {
        Apply(new Project(Branch, await _repository.SaveAsync(Branch, changes, revision)));
        return Current;
    }

    public Task<string> LoadBodyAsync(string documentId) => _repository.LoadBodyAsync(Branch, documentId);

    private void Apply(Project next)
    {
        var previous = Current;
        var changed = previous.Fingerprint() != next.Fingerprint();
        Current = next;
        if (!changed) return;
        Events.Publish();
        Changed?.Invoke(next);
        var documents = previous.Documents.Where(document => next.Document(document.Id) is null).ToArray();
        if (documents.Length > 0) DocumentsRemoved?.Invoke(documents);
        var folders = previous.Folders.Where(folder => next.Folder(folder.Id) is null).ToArray();
        if (folders.Length > 0) FoldersRemoved?.Invoke(folders);
    }

    public async ValueTask DisposeAsync()
    {
        if (_lease is not null) await _lease.DisposeAsync();
        _gate.Dispose();
    }
}
