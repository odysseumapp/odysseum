using Odysseum.Server.API.Models;

namespace Odysseum.Server.Repositories;

public interface IDocumentVersionRepository
{
    Task SaveAsync(string id, byte[] bytes);
    Task<IReadOnlyList<SnapshotInfo>> ListAsync(string id);
    Task<string> ReadAsync(string id, string snapshot);
}
