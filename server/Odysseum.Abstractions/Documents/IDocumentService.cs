using Odysseum.Abstractions.Documents.Events;
using Odysseum.Abstractions.Projects;

namespace Odysseum.Abstractions.Documents;

public interface IDocumentService
{
    event EventHandler<DocumentEventArgs>? DocumentCreated;
    event EventHandler<DocumentEventArgs>? DocumentSaved;
    event EventHandler<DocumentEventArgs>? DocumentMoved;
    event EventHandler<DocumentEventArgs>? DocumentRemoved;

    Task<IReadOnlyList<IDocument>> ListAsync(IProject project);
    Task<IDocument> GetAsync(IProject project, string id);
    /// <summary>The document with its body loaded.</summary>
    Task<IDocument> OpenAsync(ProjectBranch branch, string id);
    /// <summary>Every document with its body loaded, in manuscript order.</summary>
    Task<IReadOnlyList<IDocument>> OpenAllAsync(ProjectBranch branch);
    Task<IDocument> CreateAsync(ProjectBranch branch, string folderId, string title, string? body = null);
    Task<IDocument> SaveBodyAsync(ProjectBranch branch, string id, string body, string expectedRevision);
    Task<IDocument> UpdateAsync(ProjectBranch branch, string id, DocumentDetails details, string expectedRevision);
    /// <summary>Moves the document to another folder, renames it, or both. A null title keeps the file name.</summary>
    Task<IDocument> MoveAsync(ProjectBranch branch, string id, string targetFolderId, string? title, string expectedRevision);
}
