namespace Odysseum.Abstractions;

/// <summary>
/// One project's storage. Callers address documents and folders by ID and never learn where or how they are kept.
/// The host serializes calls; an implementation may assume one operation at a time.
///
/// Two revisions guard against lost updates: each <see cref="Document.Revision"/> covers that document's body, and
/// <see cref="Project.Revision"/> covers all metadata. A stale revision fails with <see cref="WorkspaceError.Conflict"/>.
/// Every mutation returns the revision the caller now holds.
///
/// Disposing releases whatever the implementation holds on the project (a lock, a connection).
/// </summary>
public interface IProjectStore : IDisposable
{
    /// <summary>Reads the whole project, reconciling with anything changed outside the store since last time.</summary>
    Task<Project> LoadAsync();

    /// <summary>Persists changed metadata: document details, order, links, folder layout, settings. Bodies and tree
    /// structure are untouched; use the operations below for those. Returns the new project revision.</summary>
    Task<string> CommitAsync(Project candidate, string expectedRevision);

    /// <summary>Replaces a document's body. Returns the document's new revision.</summary>
    Task<string> SaveAsync(DocumentId id, string body, string expectedRevision);

    /// <summary>Creates a document. The store chooses where it lives and what it is called. A null body means the
    /// store's starter content for that kind of document.</summary>
    Task<Document> CreateAsync(FolderId folder, string title, string? body = null);

    /// <summary>Moves a document to another folder, optionally under a new title. Returns the document's new revision.</summary>
    Task<string> MoveAsync(DocumentId id, FolderId target, string? title, string expectedRevision);

    /// <summary>Creates a folder. Returns it, including the document that opens with it.</summary>
    Task<Folder> CreateFolderAsync(FolderId parent, string name);

    /// <summary>Removes an empty folder. Its own document and layout go with it. Returns the new project revision.</summary>
    Task<string> RemoveFolderAsync(FolderId id, string expectedRevision);

    /// <summary>Recovery snapshots of a document's body, newest first.</summary>
    Task<IReadOnlyList<HistoryEntry>> ListHistoryAsync(DocumentId id);

    /// <summary>The body a snapshot recorded.</summary>
    Task<string> ReadHistoryAsync(DocumentId id, string snapshot);
}
