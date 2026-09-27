using Odysseum.Abstractions.Projects.Events;

namespace Odysseum.Abstractions.Projects;

public interface IProjectService
{
    event EventHandler<ProjectEventArgs>? ProjectCreated;
    event EventHandler<ProjectEventArgs>? ProjectUpdated;
    event EventHandler<ProjectEventArgs>? ProjectRemoved;

    /// <summary>Every project in the workspace, also project folders that were added since the last call.</summary>
    Task<IReadOnlyList<IProject>> GetAllProjectsAsync();
    Task<IProject> GetProjectByIdAsync(string projectId);
    /// <summary>Makes a project from a template. Without a template name, the <c>Default</c> template is used.</summary>
    Task<IProject> CreateProjectAsync(string title, int? wordGoal = null, string? templateName = null);
    Task<IProject> UpdateProjectSettingsAsync(string projectId, ProjectSettings settings, string expectedETag);
}
