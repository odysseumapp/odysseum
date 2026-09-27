using Odysseum.Server.Models;

namespace Odysseum.Server.Repositories;

public interface IProjectRepository : IRepository<Project>
{
    event EventHandler<RepositoryChangeEventArgs<Project>>? ItemAdded;
    event EventHandler<RepositoryChangeEventArgs<Project>>? ItemUpdated;
    event EventHandler<RepositoryChangeEventArgs<Project>>? ItemRemoved;

    /// <summary>Reads the projects that were added to the workspace since the last read.</summary>
    Task FindNewProjectsAsync();
    /// <summary>Reads the whole project again from the storage, for example after a version was restored.</summary>
    Task ReloadProjectAsync(string projectId);
}
