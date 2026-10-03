using System.Collections.Concurrent;
using Odysseum.Abstractions.Changes;
using Odysseum.Abstractions.Exceptions;
using Odysseum.Abstractions.History;
using Odysseum.Server.Models;
using Odysseum.Server.Repositories;

namespace Odysseum.Server.Services;

/// <summary>Saved versions of projects. Every call runs under the project lock, so history work never overlaps a save.
/// After a project has had no changes for <c>versionSeconds</c>, an automatic version is saved.</summary>
public sealed class HistoryService : IHistoryService, IAsyncDisposable
{
    private readonly IWorkspaceRepository _workspace;
    private readonly IProjectHistory _history;
    private readonly IProjectLock _projectLock;
    private readonly int _versionSeconds;
    private readonly ILogger<HistoryService>? _logger;
    private readonly ConcurrentDictionary<string, DateTime> _lastChange = new(StringComparer.Ordinal);
    /// <summary>The folder names of the projects the history has opened, by project ID.</summary>
    private readonly ConcurrentDictionary<string, string> _opened = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _stopping = new();
    private Task _loop = Task.CompletedTask;

    public HistoryService(IWorkspaceRepository workspace, IProjectHistory history, IProjectLock projectLock, int versionSeconds,
        ILogger<HistoryService>? logger = null)
    {
        _workspace = workspace;
        _history = history;
        _projectLock = projectLock;
        _versionSeconds = versionSeconds;
        _logger = logger;
        workspace.Changed += OnChanged;
    }

    /// <summary>Saves an automatic version of each project that changed while the server was stopped, then starts saving
    /// automatic versions. Nothing is saved automatically when <c>versionSeconds</c> is 0.</summary>
    public async Task StartAsync()
    {
        foreach (var project in await _workspace.GetProjectsAsync()) await SaveAutomaticVersionAsync(project.Id);
        _lastChange.Clear();
        if (_versionSeconds > 0) _loop = Task.Run(() => RunAsync(_stopping.Token));
    }

    public async Task<ProjectVersion> SaveVersionAsync(string projectId, string name)
    {
        name = name?.Trim() ?? "";
        if (name.Length is 0 or > 200 || name.Contains('\n'))
            throw new WorkspaceException(WorkspaceError.Invalid, "A version name is one line of up to 200 characters.");
        var project = await FindProjectAsync(projectId);
        return await _projectLock.RunLockedAsync(projectId, async () => (await _history.SaveVersionAsync(project.Name, name))!);
    }

    public async Task<IReadOnlyList<ProjectVersion>> GetVersionsByProjectIdAsync(string projectId)
    {
        var project = await FindProjectAsync(projectId);
        return await _projectLock.RunLockedAsync(projectId, () => _history.ListVersionsAsync(project.Name));
    }

    public async Task<IReadOnlyList<ProjectVersion>> GetVersionsByDocumentIdAsync(string documentId)
    {
        var document = await FindDocumentAsync(documentId);
        var project = await FindProjectAsync(document.ProjectId);
        return await _projectLock.RunLockedAsync(project.Id, () => _history.ListVersionsAsync(project.Name, document.Path));
    }

    public async Task<string> GetDocumentTextFromVersionAsync(string documentId, string versionId)
    {
        var document = await FindDocumentAsync(documentId);
        var project = await FindProjectAsync(document.ProjectId);
        return await _projectLock.RunLockedAsync(project.Id, () => _history.ReadAsync(project.Name, versionId, document.Path));
    }

    public async Task RestoreProjectVersionAsync(string projectId, string versionId)
    {
        var project = await FindProjectAsync(projectId);
        await _projectLock.RunLockedAsync(projectId, async () =>
        {
            await _history.RestoreAsync(project.Name, versionId);
            await _workspace.ReloadProjectAsync(projectId);
        });
    }

    public async Task RestoreDocumentVersionAsync(string documentId, string versionId)
    {
        var document = await FindDocumentAsync(documentId);
        var project = await FindProjectAsync(document.ProjectId);
        await _projectLock.RunLockedAsync(project.Id, async () =>
        {
            await _history.RestoreAsync(project.Name, versionId, document.Path);
            await _workspace.ReloadProjectAsync(project.Id);
        });
    }

    /// <summary>Marks the project as changed, or closes its history when the project is gone.</summary>
    private void OnChanged(object? sender, ChangesEventArgs e)
    {
        foreach (var change in e.Changes)
        {
            if (change is not { Type: ItemType.Project, Kind: ChangeKind.Removed })
            {
                _lastChange[change.ProjectId] = DateTime.UtcNow;
                continue;
            }
            _lastChange.TryRemove(change.ProjectId, out _);
            if (_opened.TryRemove(change.ProjectId, out var name)) _history.Close(name);
        }
    }

    private async Task SaveAutomaticVersionAsync(string projectId)
    {
        try
        {
            var project = await FindProjectAsync(projectId);
            await _projectLock.RunLockedAsync(projectId, () => _history.SaveVersionAsync(project.Name, null));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or WorkspaceException)
        { _logger?.LogWarning("Automatic version of project {Project} was deferred: {Message}", projectId, ex.Message); }
    }

    /// <summary>The project, which the history is about to open.</summary>
    private async Task<Project> FindProjectAsync(string projectId)
    {
        var project = await _workspace.GetAsync<Project>(projectId) ?? throw new WorkspaceException(WorkspaceError.NotFound, "No project has that ID.");
        _opened[project.Id] = project.Name;
        return project;
    }

    private async Task<Document> FindDocumentAsync(string documentId) => await _workspace.GetAsync<Document>(documentId)
        ?? throw new WorkspaceException(WorkspaceError.NotFound, "This document was removed or moved outside the workspace.");

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var now = DateTime.UtcNow;
                foreach (var (projectId, at) in _lastChange.ToArray())
                {
                    if (now - at < TimeSpan.FromSeconds(_versionSeconds) || !_lastChange.TryRemove(projectId, out _)) continue;
                    await SaveAutomaticVersionAsync(projectId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        try { await _loop; } catch (OperationCanceledException) { }
        _stopping.Dispose();
    }
}
