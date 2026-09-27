using Odysseum.Abstractions.Exceptions;
using Odysseum.Abstractions.Projects;
using Odysseum.Abstractions.Projects.Events;
using Odysseum.Server.Models;
using Odysseum.Server.Repositories;
using Odysseum.Server.Services.Projects;

namespace Odysseum.Server.Services;

public sealed class ProjectService : IProjectService
{
    private readonly ProjectSessions _sessions;

    public ProjectService(ProjectSessions sessions)
    {
        _sessions = sessions;
        sessions.Changed += (_, project) => ProjectChanged?.Invoke(this, new(project));
    }

    public event EventHandler<ProjectEventArgs>? ProjectCreated;
    public event EventHandler<ProjectEventArgs>? ProjectUpdated;
    public event EventHandler<ProjectEventArgs>? ProjectChanged;

    public Task<IReadOnlyList<ProjectInfo>> ListAsync() => _sessions.ListAsync();

    public async Task<IProject> GetAsync(ProjectBranch branch) => await (await _sessions.OpenAsync(branch)).ReloadAsync();

    /// <summary>Finds the project by ID, or by name when the value is not a known ID.</summary>
    public async Task<IProject> GetAsync(string nameOrId)
    {
        if (Guid.TryParse(nameOrId, out _) && await _sessions.FindNameAsync(nameOrId) is { } name) return await GetAsync(ProjectBranch.Main(name));
        return await GetAsync(ProjectBranch.Main(nameOrId));
    }

    public async Task<IProject> CreateAsync(string title, int? wordGoal = null, string? template = null)
    {
        var info = await _sessions.CreateAsync(title, wordGoal, template);
        var project = await GetAsync(ProjectBranch.Main(info.Name));
        ProjectCreated?.Invoke(this, new(project));
        return project;
    }

    public async Task<IProject> SaveSettingsAsync(ProjectBranch branch, ProjectSettings settings, string expectedRevision)
    {
        var session = await _sessions.OpenAsync(branch);
        return await session.RunAsync<IProject>(async () =>
        {
            var current = session.Current;
            ContentRevision.Check(current.Revision, expectedRevision);
            var validated = Settings.ProjectSettings.From(new Settings.ProjectSettings
            {
                Title = settings.Title ?? current.Title,
                WordGoal = settings.WordGoal ?? current.WordGoal,
                DefaultSceneWordGoal = settings.DefaultSceneWordGoal ?? current.DefaultSceneWordGoal,
            }, out var error);
            if (error is not null) throw new WorkspaceException(WorkspaceError.Invalid, error);
            var saved = await session.SaveAsync(new Changes(validated, [], [], []), current.Revision);
            ProjectUpdated?.Invoke(this, new(saved));
            return saved;
        });
    }
}
