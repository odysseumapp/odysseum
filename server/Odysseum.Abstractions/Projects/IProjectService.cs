using Odysseum.Abstractions.Changes;
using Odysseum.Abstractions.Documents;
using Odysseum.Abstractions.Folders;
using Odysseum.Abstractions.Items;
using Odysseum.Abstractions.Links;

namespace Odysseum.Abstractions.Projects;

/// <summary>The projects of the workspace, with their folders, documents and links. Items are read-only snapshots; a
/// later read gives new ones. Each change gives the ETag the caller last saw, and is refused when the item has changed.</summary>
public interface IProjectService
{
    /// <summary>Raised once for each write, and for each read of a project that found changes, also changes that other
    /// programs made to the files.</summary>
    event EventHandler<ChangesEventArgs>? Changed;

    /// <summary>The <see cref="IProject"/>, <see cref="IFolder"/>, <see cref="IDocument"/> or <see cref="ILink"/> with
    /// that ID. Throws <see cref="Exceptions.WorkspaceError.NotFound"/> when there is none.</summary>
    Task<T> GetAsync<T>(string id) where T : IItem;
    /// <summary>The project's <see cref="IFolder"/>, <see cref="IDocument"/> or <see cref="ILink"/> items, in no fixed order.</summary>
    Task<IReadOnlyList<T>> GetAllAsync<T>(string projectId) where T : IProjectItem;
    /// <summary>Every project in the workspace by title, also project folders that were added since the last call.</summary>
    Task<IReadOnlyList<IProject>> GetProjectsAsync();
    /// <summary>Every document of the project in tree order: a folder's own document first, then its children in order,
    /// with each subfolder's documents in its place.</summary>
    Task<IReadOnlyList<IDocument>> GetDocumentsInOrderAsync(string projectId);
    /// <summary>The documents among the folder's children, in order. The folder's own document is not included.</summary>
    Task<IReadOnlyList<IDocument>> GetChildrenAsync(string folderId);
    Task<IReadOnlyList<ILink>> GetLinksForDocumentAsync(string documentId);
    Task<string> GetDocumentTextAsync(string documentId);
    /// <summary>The documents whose title, synopsis, notes or text contain <paramref name="text"/>, at most 50.</summary>
    Task<IReadOnlyList<DocumentSearchResult>> SearchDocumentsAsync(string projectId, string text);
    /// <summary>One Markdown file with the project's scenes in tree order.</summary>
    Task<string> ExportMarkdownAsync(string projectId);

    /// <summary>Makes a project from a template. Without a template name, the <c>Default</c> template is used.</summary>
    Task<IProject> CreateProjectAsync(string title, int? wordGoal = null, string? templateName = null);
    Task<IProject> UpdateProjectSettingsAsync(string projectId, ProjectSettings settings, string expectedETag);
    /// <summary>Saves the project's folders, documents and layouts as a template that <see cref="CreateProjectAsync"/> can
    /// use. A template with the same name is replaced.</summary>
    Task SaveAsTemplateAsync(string projectId, string name);

    /// <summary>Makes an empty folder, with its own hidden document, at the end of the parent folder's children.</summary>
    Task<IFolder> CreateFolderAsync(string parentFolderId, string name);
    Task<IFolder> UpdateFolderLayoutAsync(string folderId, FolderLayout layout, string expectedETag);
    /// <summary>Puts the folder at <c>index</c> among the target folder's children. The index is clamped.</summary>
    Task<FolderMoveResult> MoveFolderToFolderAsync(string folderId, string targetFolderId, int index, string expectedETag);
    /// <summary>Deletes a folder that has no children. View settings in other folders that name it are cleared.</summary>
    Task DeleteFolderAsync(string folderId, string expectedETag);

    /// <summary>Makes a document at the end of the folder's children. Its file name comes from the title.</summary>
    Task<IDocument> CreateDocumentAsync(string folderId, string title, string? text = null);
    Task<IDocument> UpdateDocumentTextAsync(string documentId, string text, string expectedETag);
    Task<IDocument> UpdateDocumentDetailsAsync(string documentId, DocumentDetails details, string expectedETag);
    /// <summary>Changes the file name. The new file name comes from <paramref name="name"/> and keeps the extension.</summary>
    Task<IDocument> RenameDocumentAsync(string documentId, string name, string expectedETag);
    /// <summary>Puts the document at <c>index</c> among the target folder's children. The index is clamped.</summary>
    Task<DocumentMoveResult> MoveDocumentToFolderAsync(string documentId, string targetFolderId, int index, string expectedETag);

    /// <summary>Links two documents of the same project. A second link between the same two documents is refused.</summary>
    Task<ILink> CreateLinkAsync(string firstDocumentId, string secondDocumentId, string note = "");
    Task<ILink> UpdateLinkNoteAsync(string linkId, string note, string expectedETag);
    Task DeleteLinkAsync(string linkId, string expectedETag);
}
