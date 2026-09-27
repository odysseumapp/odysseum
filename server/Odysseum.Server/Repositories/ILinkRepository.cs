using Odysseum.Server.Models;

namespace Odysseum.Server.Repositories;

public interface ILinkRepository : IRepository<Link>
{
    event EventHandler<RepositoryChangeEventArgs<Link>>? ItemAdded;
    event EventHandler<RepositoryChangeEventArgs<Link>>? ItemUpdated;
    event EventHandler<RepositoryChangeEventArgs<Link>>? ItemRemoved;

    Task<IReadOnlyList<Link>> GetLinksByProjectIdAsync(string projectId);
    Task<IReadOnlyList<Link>> GetLinksByDocumentIdAsync(string documentId);
}
