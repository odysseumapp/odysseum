using System.Collections.Concurrent;
using Odysseum.Abstractions.Exceptions;
using Odysseum.Abstractions.History;
using Odysseum.Abstractions.Projects;
using Odysseum.Server.Models;
using Odysseum.Server.Repositories;
using Odysseum.Server.Services.Projects;

namespace Odysseum.Server.Services;

/// <summary>Saved versions of open projects. Every call runs under the project's session lock, so history work never
/// overlaps a save. After a project has been quiet for <c>versionSeconds</c>, an automatic version is saved.</summary>
public sealed class HistoryService : IHistoryService, IAsyncDisposable
{
    private readonly ProjectSessions _sessions;
    private readonly IProjectHistory _history;
    private readonly int _versionSeconds;
    private readonly ILogger<HistoryService>? _logger;
    private readonly ConcurrentDictionary<ProjectBranch, DateTime> _lastChange = new(ProjectBranchComparer.Instance);
    private readonly CancellationTokenSource _stopping = new();
    private Task _loop = Task.CompletedTask;

    public HistoryService(ProjectSessions sessions, IProjectHistory history, int versionSeconds, ILogger<HistoryService>? logger = null)
    {
        _sessions = sessions;
        _history = history;
        _versionSeconds = versionSeconds;
        _logger = logger;
        sessions.Changed += (session, _) => _lastChange[session.Branch] = DateTime.UtcNow;
    }

    /// <summary>Starts saving automatic versions. Nothing is saved when <c>versionSeconds</c> is 0.</summary>
    public void Start()
    {
        if (_versionSeconds > 0) _loop = Task.Run(() => RunAsync(_stopping.Token));
    }

    public async Task<ProjectVersion> SaveVersionAsync(ProjectBranch branch, string label)
    {
        label = label?.Trim() ?? "";
        if (label.Length is 0 or > 200 || label.Contains('\n'))
            throw new WorkspaceException(WorkspaceError.Invalid, "A version name is one line of up to 200 characters.");
        var session = await _sessions.OpenAsync(branch);
        return await session.RunAsync(async () => (await _history.SaveVersionAsync(branch, label))!, reload: false);
    }

    public async Task<IReadOnlyList<ProjectVersion>> ListVersionsAsync(ProjectBranch branch, string? documentId = null)
    {
        var session = await _sessions.OpenAsync(branch);
        return await session.RunAsync(() => _history.ListVersionsAsync(branch, PathOf(session.Current, documentId)), reload: false);
    }

    public async Task<IProject> RestoreAsync(ProjectBranch branch, string versionId, string? documentId = null)
    {
        var session = await _sessions.OpenAsync(branch);
        return await session.RunAsync<IProject>(async () =>
        {
            await _history.RestoreAsync(branch, versionId, PathOf(session.Current, documentId));
            return await session.RefreshAsync();
        });
    }

    public async Task<string> ReadAsync(ProjectBranch branch, string versionId, string documentId)
    {
        var session = await _sessions.OpenAsync(branch);
        return await session.RunAsync(() => _history.ReadAsync(branch, versionId, PathOf(session.Current, documentId)!), reload: false);
    }

    private static string? PathOf(Project project, string? documentId) => documentId is null ? null
        : (project.Document(documentId) ?? throw new WorkspaceException(WorkspaceError.NotFound, "This document was removed or moved outside the workspace. Your browser draft is still available.")).Path;

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var now = DateTime.UtcNow;
                foreach (var (branch, at) in _lastChange.ToArray())
                {
                    if (now - at < TimeSpan.FromSeconds(_versionSeconds) || !_lastChange.TryRemove(branch, out _)) continue;
                    if (_sessions.Get(branch) is not { } session) continue;
                    try { await session.RunAsync(() => _history.SaveVersionAsync(branch, null), reload: false); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or WorkspaceException)
                    { _logger?.LogWarning("Automatic version of {Project} deferred: {Message}", branch.Project, ex.Message); }
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
