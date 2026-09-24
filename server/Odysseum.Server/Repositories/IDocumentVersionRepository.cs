using Odysseum.Server.API.Models;
using Odysseum.Server.Models;

namespace Odysseum.Server.Repositories;

public interface IDocumentVersionRepository
{
    Task SaveAsync(string id, byte[] bytes);
    Task<IReadOnlyList<SnapshotInfo>> ListAsync(string id);
    Task<IReadOnlyList<DocumentVersion>> ListAsync(Document document);
    Task<string> ReadAsync(string id, string snapshot);
}
