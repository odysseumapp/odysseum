using System.Collections.Concurrent;
using Odysseum.Abstractions.Exceptions;
using Odysseum.Abstractions.Projects;
using Odysseum.Server.Models;
using Odysseum.Server.Repositories;
using Odysseum.Server.Services.Documents;
using Odysseum.Server.Services.Templates;
using ProjectSettings = Odysseum.Server.Settings.ProjectSettings;

namespace Odysseum.Server.Services.Projects;

/// <summary>The open project sessions, one per project branch. It opens a session on first use, reloads a session when
/// the watcher reports an external change, and closes sessions when the server stops.</summary>
public sealed class ProjectSessions : IAsyncDisposable
{
    private readonly IProjectRepository _repository;
    private readonly IProjectWatcher _watcher;
    private readonly IProjectHistory _history;
    private readonly ITemplateRepository? _templates;
    private readonly ILogger<ProjectSessions>? _logger;
    private readonly ProjectTemplateService _templateService = new();
    private readonly ConcurrentDictionary<ProjectBranch, Lazy<Task<ProjectSession>>> _open = new(ProjectBranchComparer.Instance);

    public ProjectSessions(IProjectRepository repository, IProjectWatcher watcher, IProjectHistory history, ITemplateRepository? templates = null, ILogger<ProjectSessions>? logger = null)
    {
        _repository = repository;
        _watcher = watcher;
        _history = history;
        _templates = templates;
        _logger = logger;
        watcher.Changed += OnExternalChange;
    }

    public event Action<ProjectSession>? Opened;
    public event Action<ProjectSession, Project>? Changed;
    public event Action<ProjectSession, IReadOnlyList<Document>>? DocumentsRemoved;
    public event Action<ProjectSession, IReadOnlyList<Folder>>? FoldersRemoved;

    public Task<IReadOnlyList<ProjectInfo>> ListAsync() => _repository.ListAsync();

    /// <summary>The open session for that branch, or null when it is not open.</summary>
    public ProjectSession? Get(ProjectBranch branch) =>
        _open.TryGetValue(branch, out var lazy) && lazy.IsValueCreated && lazy.Value.IsCompletedSuccessfully ? lazy.Value.Result : null;

    public async Task<ProjectSession> OpenAsync(ProjectBranch branch)
    {
        if (!branch.IsMain) throw new WorkspaceException(WorkspaceError.Invalid, "Only the main branch exists.");
        if (!await _repository.ExistsAsync(branch))
        {
            if (_open.TryRemove(branch, out var stale) && stale.IsValueCreated && stale.Value.IsCompletedSuccessfully)
                await CloseAsync(stale.Value.Result);
            throw new WorkspaceException(WorkspaceError.NotFound, "That project no longer exists in the workspace.");
        }
        var lazy = _open.GetOrAdd(branch, key => new Lazy<Task<ProjectSession>>(async () =>
        {
            var session = new ProjectSession(key, _repository, new ProjectEvents());
            try { await session.OpenAsync(); }
            catch
            {
                await session.DisposeAsync();
                throw;
            }
            session.Changed += project => Changed?.Invoke(session, project);
            session.DocumentsRemoved += documents => DocumentsRemoved?.Invoke(session, documents);
            session.FoldersRemoved += folders => FoldersRemoved?.Invoke(session, folders);
            _watcher.Watch(key);
            if (session.Current.Documents.Count > 0)
                await session.RunAsync(() => _history.SaveVersionAsync(key, null), reload: false);
            Opened?.Invoke(session);
            return session;
        }));
        try { return await lazy.Value; }
        catch
        {
            _open.TryRemove(new KeyValuePair<ProjectBranch, Lazy<Task<ProjectSession>>>(branch, lazy));
            throw;
        }
    }

    public async Task CloseAsync(ProjectBranch branch)
    {
        if (!_open.TryRemove(branch, out var lazy) || !lazy.IsValueCreated) return;
        ProjectSession session;
        try { session = await lazy.Value; }
        catch (WorkspaceException) { return; }
        await CloseAsync(session);
    }

    private async Task CloseAsync(ProjectSession session)
    {
        _watcher.Unwatch(session.Branch);
        _history.Close(session.Branch);
        await session.DisposeAsync();
    }

    /// <summary>Makes a project from a template and opens it.</summary>
    public async Task<ProjectInfo> CreateAsync(string title, int? wordGoal = null, string? templateName = null)
    {
        title = DocumentRules.ValidateTitle(title);
        var template = _templates?.Get(string.IsNullOrWhiteSpace(templateName) ? TemplateRepository.DefaultName : templateName)
            ?? (string.IsNullOrWhiteSpace(templateName) || TemplateRepository.IsDefault(templateName) ? TemplateRepository.Default()
                : throw new WorkspaceException(WorkspaceError.NotFound, $"There is no project template called '{templateName}'."));
        var settings = ProjectSettings.From(new ProjectSettings
        {
            Title = title, WordGoal = wordGoal ?? template.Settings.WordGoal, DefaultSceneWordGoal = template.Settings.DefaultSceneWordGoal,
        }, out var error);
        if (error is not null) throw new WorkspaceException(WorkspaceError.Invalid, error);
        var info = await _repository.CreateAsync(title);
        var session = await OpenAsync(ProjectBranch.Main(info.Name));
        await _templateService.ApplyAsync(session, template, settings);
        await session.RunAsync(() => _history.SaveVersionAsync(session.Branch, null), reload: false);
        return info with { Title = session.Current.Title, Id = session.Current.Id };
    }

    private void OnExternalChange(ProjectBranch branch)
    {
        if (Get(branch) is { } session) _ = ReloadQuietlyAsync(session);
    }

    private async Task ReloadQuietlyAsync(ProjectSession session)
    {
        try { await session.ReloadAsync(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or WorkspaceException)
        { _logger?.LogWarning("Reload of {Project} deferred: {Message}", session.Name, ex.Message); }
    }

    public async ValueTask DisposeAsync()
    {
        _watcher.Changed -= OnExternalChange;
        foreach (var branch in _open.Keys.ToArray()) await CloseAsync(branch);
    }
}
