using Odysseum.Abstractions.Projects;

namespace Odysseum.Server.Repositories;

/// <summary>Reports changes that other programs make to a watched project. Changes this process wrote are not reported.</summary>
public interface IProjectWatcher : IAsyncDisposable
{
    event Action<ProjectBranch>? Changed;
    void Watch(ProjectBranch branch);
    void Unwatch(ProjectBranch branch);
}
