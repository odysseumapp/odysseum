namespace Odysseum.Abstractions;

public interface IProjectStore : IDisposable
{
    Task<Project> LoadAsync();

    Task<string> CommitAsync(Project candidate, string expectedRevision);

    Task<string> SaveAsync(DocumentId id, string body, string expectedRevision);

    Task<Document> CreateAsync(FolderId folder, string title, string? body = null);

    Task<string> MoveAsync(DocumentId id, FolderId target, string? title, string expectedRevision);

    Task<Folder> CreateFolderAsync(FolderId parent, string name);

    Task<string> RemoveFolderAsync(FolderId id, string expectedRevision);

    Task<IReadOnlyList<HistoryEntry>> ListHistoryAsync(DocumentId id);

    Task<string> ReadHistoryAsync(DocumentId id, string snapshot);
}
