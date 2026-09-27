using Odysseum.Abstractions.Documents.Events;

namespace Odysseum.Abstractions.Documents;

public interface IDocumentService
{
    event EventHandler<DocumentEventArgs>? DocumentCreated;
    /// <summary>The text, the details or the name changed.</summary>
    event EventHandler<DocumentEventArgs>? DocumentUpdated;
    event EventHandler<DocumentEventArgs>? DocumentMoved;
    event EventHandler<DocumentEventArgs>? DocumentRemoved;

    Task<IDocument> GetDocumentByIdAsync(string documentId);
    /// <summary>Every document of the project in manuscript order: a folder's own document first, then its children in
    /// order, with each subfolder's documents in its place.</summary>
    Task<IReadOnlyList<IDocument>> GetDocumentsByProjectIdAsync(string projectId);
    /// <summary>The documents among the folder's children, in order. The folder's own document is not included.</summary>
    Task<IReadOnlyList<IDocument>> GetDocumentsByFolderIdAsync(string folderId);
    Task<string> GetDocumentTextByIdAsync(string documentId);
    /// <summary>Makes a document at the end of the folder's children. Its file name comes from the title.</summary>
    Task<IDocument> CreateDocumentAsync(string folderId, string title, string? text = null);
    Task<IDocument> UpdateDocumentTextAsync(string documentId, string text, string expectedETag);
    Task<IDocument> UpdateDocumentDetailsAsync(string documentId, DocumentDetails details, string expectedETag);
    /// <summary>Changes the file name. The new file name comes from <paramref name="name"/> and keeps the extension.</summary>
    Task<IDocument> RenameDocumentAsync(string documentId, string name, string expectedETag);
    /// <summary>Puts the document at <c>index</c> among the target folder's children. The index is clamped.</summary>
    Task<DocumentMoveResult> MoveDocumentToFolderAsync(string documentId, string targetFolderId, int index, string expectedETag);
    /// <summary>The documents whose title, synopsis, notes or text contain <paramref name="text"/>, at most 50.</summary>
    Task<IReadOnlyList<DocumentSearchResult>> SearchDocumentsAsync(string projectId, string text);
}
