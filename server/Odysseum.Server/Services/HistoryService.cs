using System.Collections.Concurrent;
using Odysseum.Abstractions.Exceptions;
using Odysseum.Abstractions.History;
using Odysseum.Server.Models;
using Odysseum.Server.Repositories;

namespace Odysseum.Server.Services;

/// <summary>Saved versions of projects. Every call runs under the project lock, so history work never overlaps a save.
/// After a project has had no changes for <c>versionSeconds</c>, an automatic version is saved.</summary>
public sealed class HistoryService : IHistoryService, IAsyncDisposable
{
    private readonly IProjectRepository _projects;
    private readonly IDocumentPlaceRepository _documentPlaces;
    private readonly IProjectHistory _history;
    private readonly IProjectLock _projectLock;
    private readonly int _versionSeconds;
    private readonly ILogger<HistoryService>? _logger;
    private readonly ConcurrentDictionary<string, DateTime> _lastChange = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _stopping = new();
    private Task _loop = Task.CompletedTask;

    public HistoryService(IProjectRepository projects, IFolderRepository folders, IDocumentRepository documents, ILinkRepository links,
        IDocumentPlaceRepository documentPlaces, IProjectHistory history, IProjectLock projectLock, int versionSeconds, ILogger<HistoryService>? logger = null)
    {
        _projects = projects;
        _documentPlaces = documentPlaces;
        _history = history;
        _projectLock = projectLock;
        _versionSeconds = versionSeconds;
        _logger = logger;
        projects.ItemAdded += (_, change) => MarkChanged(change.Item.Id);
        projects.ItemUpdated += (_, change) => MarkChanged(change.Item.Id);
        projects.ItemRemoved += (_, change) => Forget(change.Item);
        folders.ItemAdded += (_, change) => MarkChanged(change.Item.ProjectId);
        folders.ItemUpdated += (_, change) => MarkChanged(change.Item.ProjectId);
        folders.ItemRemoved += (_, change) => MarkChanged(change.Item.ProjectId);
        documents.ItemAdded += (_, change) => MarkChanged(change.Item.ProjectId);
        documents.ItemUpdated += (_, change) => MarkChanged(change.Item.ProjectId);
        documents.ItemRemoved += (_, change) => MarkChanged(change.Item.ProjectId);
        links.ItemAdded += (_, change) => MarkChanged(change.Item.ProjectId);
        links.ItemUpdated += (_, change) => MarkChanged(change.Item.ProjectId);
        links.ItemRemoved += (_, change) => MarkChanged(change.Item.ProjectId);
    }

    /// <summary>Saves an automatic version of each project that changed while the server was stopped, then starts saving
    /// automatic versions. Nothing is saved automatically when <c>versionSeconds</c> is 0.</summary>
    public async Task StartAsync()
    {
        foreach (var project in await _projects.GetAllAsync()) await SaveAutomaticVersionAsync(project.Id);
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
        var place = await _documentPlaces.GetPlaceByDocumentIdAsync(documentId);
        var project = await FindProjectAsync(place.ProjectId);
        return await _projectLock.RunLockedAsync(project.Id, () => _history.ListVersionsAsync(project.Name, place.Path));
    }

    public async Task<string> GetDocumentTextFromVersionAsync(string documentId, string versionId)
    {
        var place = await _documentPlaces.GetPlaceByDocumentIdAsync(documentId);
        var project = await FindProjectAsync(place.ProjectId);
        return await _projectLock.RunLockedAsync(project.Id, () => _history.ReadAsync(project.Name, versionId, place.Path));
    }

    public async Task RestoreProjectVersionAsync(string projectId, string versionId)
    {
        var project = await FindProjectAsync(projectId);
        await _projectLock.RunLockedAsync(projectId, async () =>
        {
            await _history.RestoreAsync(project.Name, versionId);
            await _projects.ReloadProjectAsync(projectId);
        });
    }

    public async Task RestoreDocumentVersionAsync(string documentId, string versionId)
    {
        var place = await _documentPlaces.GetPlaceByDocumentIdAsync(documentId);
        var project = await FindProjectAsync(place.ProjectId);
        await _projectLock.RunLockedAsync(project.Id, async () =>
        {
            await _history.RestoreAsync(project.Name, versionId, place.Path);
            await _projects.ReloadProjectAsync(project.Id);
        });
    }

    private void MarkChanged(string projectId) => _lastChange[projectId] = DateTime.UtcNow;

    private void Forget(Project project)
    {
        _lastChange.TryRemove(project.Id, out _);
        _history.Close(project.Name);
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

    private async Task<Project> FindProjectAsync(string projectId) => await _projects.GetByIdAsync(projectId)
        ?? throw new WorkspaceException(WorkspaceError.NotFound, "No project has that ID.");

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
