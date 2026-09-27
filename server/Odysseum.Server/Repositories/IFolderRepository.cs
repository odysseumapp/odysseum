using Odysseum.Server.Models;

namespace Odysseum.Server.Repositories;

public interface IFolderRepository : IRepository<Folder>
{
    event EventHandler<RepositoryChangeEventArgs<Folder>>? ItemAdded;
    event EventHandler<RepositoryChangeEventArgs<Folder>>? ItemUpdated;
    event EventHandler<RepositoryChangeEventArgs<Folder>>? ItemRemoved;

    Task<IReadOnlyList<Folder>> GetFoldersByProjectIdAsync(string projectId);
}
