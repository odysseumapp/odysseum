using Odysseum.Abstractions.Exceptions;
using Odysseum.Abstractions.Projects;
using Odysseum.Abstractions.Projects.Events;
using Odysseum.Server.Models;
using Odysseum.Server.Repositories;
using Odysseum.Server.Services.Documents;
using Odysseum.Server.Services.Templates;
using ProjectSettings = Odysseum.Abstractions.Projects.ProjectSettings;
using ValidatedSettings = Odysseum.Server.Settings.ProjectSettings;

namespace Odysseum.Server.Services;

public sealed class ProjectService : IProjectService
{
    private readonly IProjectRepository _projects;
    private readonly IProjectLock _projectLock;
    private readonly ITemplateRepository? _templates;
    private readonly ProjectTemplateService _templateService;

    public ProjectService(IProjectRepository projects, IProjectLock projectLock, ProjectTemplateService templateService, ITemplateRepository? templates = null)
    {
        _projects = projects;
        _projectLock = projectLock;
        _templateService = templateService;
        _templates = templates;
        projects.ItemAdded += (_, change) => ProjectCreated?.Invoke(this, new(change.Item));
        projects.ItemUpdated += (_, change) => ProjectUpdated?.Invoke(this, new(change.Item));
        projects.ItemRemoved += (_, change) => ProjectRemoved?.Invoke(this, new(change.Item));
    }

    public event EventHandler<ProjectEventArgs>? ProjectCreated;
    public event EventHandler<ProjectEventArgs>? ProjectUpdated;
    public event EventHandler<ProjectEventArgs>? ProjectRemoved;

    public async Task<IReadOnlyList<IProject>> GetAllProjectsAsync()
    {
        await _projects.FindNewProjectsAsync();
        return (await _projects.GetAllAsync())
            .OrderBy(project => project.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(project => project.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<IProject> GetProjectByIdAsync(string projectId) => await FindAsync(projectId);

    public async Task<IProject> CreateProjectAsync(string title, int? wordGoal = null, string? templateName = null)
    {
        title = DocumentRules.ValidateTitle(title);
        var template = FindTemplate(templateName);
        var settings = Validate(new ProjectSettings(title, wordGoal ?? template.Settings.WordGoal, template.Settings.DefaultSceneWordGoal));
        var project = await _projects.AddAsync(new Project("", "", settings.Title, settings.WordGoal, settings.DefaultSceneWordGoal, "", default, null));
        await _projectLock.RunLockedAsync(project.Id, () => _templateService.ApplyTemplateAsync(project, template));
        return await FindAsync(project.Id);
    }

    public async Task<IProject> UpdateProjectSettingsAsync(string projectId, ProjectSettings settings, string expectedETag)
    {
        var project = await FindAsync(projectId);
        var validated = Validate(settings);
        return await _projects.UpdateAsync(project with
        {
            Title = validated.Title, WordGoal = validated.WordGoal, DefaultSceneWordGoal = validated.DefaultSceneWordGoal,
        }, expectedETag);
    }

    private async Task<Project> FindAsync(string projectId) => await _projects.GetByIdAsync(projectId)
        ?? throw new WorkspaceException(WorkspaceError.NotFound, "No project has that ID.");

    private ProjectTemplate FindTemplate(string? name)
    {
        var useDefault = string.IsNullOrWhiteSpace(name);
        return _templates?.Get(useDefault ? TemplateRepository.DefaultName : name!)
            ?? (useDefault || TemplateRepository.IsDefault(name!) ? TemplateRepository.Default()
                : throw new WorkspaceException(WorkspaceError.NotFound, $"There is no project template called '{name}'."));
    }

    private static ValidatedSettings Validate(ProjectSettings settings)
    {
        var validated = ValidatedSettings.From(new ValidatedSettings
        {
            Title = settings.Title, WordGoal = settings.WordGoal, DefaultSceneWordGoal = settings.DefaultSceneWordGoal,
        }, out var error);
        return error is null ? validated : throw new WorkspaceException(WorkspaceError.Invalid, error);
    }
}
