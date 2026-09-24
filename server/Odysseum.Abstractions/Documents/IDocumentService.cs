using Odysseum.Abstractions.Documents.Events;
using Odysseum.Abstractions.Folders;
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
    Task<IDocument> CreateAsync(IFolder folder, string title, string? body = null);
    Task<IDocument> SaveBodyAsync(IDocument document, string body, string expectedRevision);
    Task<IDocument> UpdateAsync(IDocument document, DocumentDetails details, string expectedRevision);
    Task<IDocument> MoveAsync(IDocument document, IFolder target, string? title, string expectedRevision);
    Task<IReadOnlyList<IDocumentVersion>> ListVersionsAsync(IDocument document);
}
