using Odysseum.Abstractions.Exceptions;
using Odysseum.Abstractions.Projects;
using Odysseum.Abstractions.Projects.Events;
using Odysseum.Server.API.Models;
using Odysseum.Server.Models;
using Odysseum.Server.Repositories;

namespace Odysseum.Server.Services;

public sealed class ProjectService : IProjectService
{
    private readonly ProjectLibrary _library;
    private readonly ProjectRepository _projects;

    public ProjectService(ProjectLibrary library)
    {
        _library = library;
        _projects = new ProjectRepository(library);
        library.ProjectChanged += project => ProjectChanged?.Invoke(this, new(project));
    }

    public event EventHandler<ProjectEventArgs>? ProjectCreated;
    public event EventHandler<ProjectEventArgs>? ProjectUpdated;
    public event EventHandler<ProjectEventArgs>? ProjectChanged;

    public async Task<IReadOnlyList<IProject>> ListAsync() => await _projects.GetAllAsync();

    public async Task<IProject> GetAsync(string id)
    {
        if (await _projects.GetAsync(id) is { } bySlug) return bySlug;
        var listed = (await _library.ListAsync()).FirstOrDefault(info => info.Id == id);
        return (listed is null ? null : await _projects.GetAsync(listed.Slug))
            ?? throw new WorkspaceException(WorkspaceError.NotFound, "That project no longer exists in the workspace.");
    }

    public async Task<IProject> CreateAsync(string title, string? template = null)
    {
        var info = await _library.CreateAsync(new CreateProjectRequest(title, null, template));
        var project = await GetAsync(info.Slug);
        ProjectCreated?.Invoke(this, new(project));
        return project;
    }

    public Task<IProject> SaveSettingsAsync(IProject project, ProjectSettings settings, string expectedRevision)
    {
        var open = OpenProject.Of(project);
        return open.RunAsync<IProject>(async () =>
        {
            ContentRevision.Check(open.Current.Revision, expectedRevision);
            var validated = Settings.ProjectSettings.From(new Settings.ProjectSettings
            {
                Title = settings.Title ?? open.Current.Title,
                WordGoal = settings.WordGoal ?? open.Current.WordGoal,
                DefaultSceneWordGoal = settings.DefaultSceneWordGoal ?? open.Current.DefaultSceneWordGoal,
            }, out var error);
            if (error is not null) throw new WorkspaceException(WorkspaceError.Invalid, error);
            await _projects.SaveAsync(open.Current.With(validated));
            ProjectUpdated?.Invoke(this, new(open.Current));
            return open.Current;
        });
    }
}
