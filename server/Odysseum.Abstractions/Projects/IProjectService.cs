using Odysseum.Abstractions.Projects.Events;

namespace Odysseum.Abstractions.Projects;

public interface IProjectService
{
    event EventHandler<ProjectEventArgs>? ProjectCreated;
    event EventHandler<ProjectEventArgs>? ProjectUpdated;
    event EventHandler<ProjectEventArgs>? ProjectChanged;

    Task<IReadOnlyList<ProjectInfo>> ListAsync();
    Task<IProject> GetAsync(ProjectBranch branch);
    /// <summary>Opens the main branch of the project with that folder name, or with that UUID.</summary>
    Task<IProject> GetAsync(string nameOrId);
    Task<IProject> CreateAsync(string title, int? wordGoal = null, string? template = null);
    /// <summary>Null settings keep their current value.</summary>
    Task<IProject> SaveSettingsAsync(ProjectBranch branch, ProjectSettings settings, string expectedRevision);
}
