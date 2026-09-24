using Odysseum.Abstractions.Projects.Events;

namespace Odysseum.Abstractions.Projects;

public interface IProjectService
{
    event EventHandler<ProjectEventArgs>? ProjectCreated;
    event EventHandler<ProjectEventArgs>? ProjectUpdated;
    event EventHandler<ProjectEventArgs>? ProjectChanged;

    Task<IReadOnlyList<IProject>> ListAsync();
    Task<IProject> GetAsync(string id);
    Task<IProject> CreateAsync(string title, string? template = null);
    Task<IProject> SaveSettingsAsync(IProject project, ProjectSettings settings, string expectedRevision);
}
