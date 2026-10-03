using Odysseum.Abstractions.Changes;
using Odysseum.Server.Models;

namespace Odysseum.Server.Repositories;

/// <summary>Every project of the workspace with its folders, documents and links. The project is the aggregate, so one
/// repository holds them all. Reads come from memory; writes go through the storage. Each write gives the ETag the
/// caller last saw, and the storage refuses it when the stored item has a different ETag.
/// A new folder or document goes where its folder (<c>ParentFolderId</c> or <c>FolderId</c>) and its <c>Name</c> say;
/// only a move puts it in another folder. The storage sets <c>Path</c>.</summary>
public interface IWorkspaceRepository
{
    /// <summary>Raised once for each write and each read of a project that changed something.</summary>
    event EventHandler<ChangesEventArgs>? Changed;

    /// <summary>The <see cref="Project"/>, <see cref="Folder"/>, <see cref="Document"/> or <see cref="Link"/> with that
    /// ID, or null.</summary>
    Task<T?> GetAsync<T>(string id) where T : class;
    /// <summary>The project's <see cref="Folder"/>, <see cref="Document"/> or <see cref="Link"/> items, in no fixed order.</summary>
    Task<IReadOnlyList<T>> GetAllAsync<T>(string projectId) where T : class;
    Task<IReadOnlyList<Project>> GetProjectsAsync();
    Task<IReadOnlyList<Link>> GetLinksForDocumentAsync(string documentId);

    /// <summary>Makes an empty project with a new ID. Its name comes from the title.</summary>
    Task<Project> AddProjectAsync(string title, int wordGoal, int defaultSceneWordGoal);
    /// <summary>Saves the title and the word goals.</summary>
    Task<Project> UpdateProjectAsync(Project project, string expectedETag);
    /// <summary>Reads the projects that were added to the workspace since the last read.</summary>
    Task FindNewProjectsAsync();
    /// <summary>Reads the whole project again from the storage, for example after a version was restored.</summary>
    Task ReloadProjectAsync(string projectId);

    /// <summary>Makes the folder with its own hidden document, at the end of the parent folder's children.</summary>
    Task<Folder> AddFolderAsync(Folder folder);
    /// <summary>Saves the pinned view, the view settings and the order of the children.</summary>
    Task<Folder> UpdateFolderAsync(Folder folder, string expectedETag);
    /// <summary>Moves the folder, with everything in it, to <paramref name="index"/> among the target folder's children.
    /// The index is clamped. A move inside the same folder only changes the order.</summary>
    Task<Folder> MoveFolderAsync(string folderId, string targetParentId, int index, string expectedETag);
    /// <summary>Deletes a folder that has no children, with its own document.</summary>
    Task DeleteFolderAsync(string folderId, string expectedETag);

    /// <summary>Makes the document's file with the text, at the end of the folder's children.</summary>
    Task<Document> AddDocumentAsync(Document document, string text);
    /// <summary>Saves the details, and renames the file when <c>Name</c> changed.</summary>
    Task<Document> UpdateDocumentAsync(Document document, string expectedETag);
    /// <summary>Moves the file, with the details, to <paramref name="index"/> among the target folder's children. The
    /// index is clamped. A move inside the same folder only changes the order.</summary>
    Task<Document> MoveDocumentAsync(string documentId, string targetFolderId, int index, string expectedETag);
    Task DeleteDocumentAsync(string documentId, string expectedETag);
    /// <summary>Reads the text from the storage. The text is not kept in memory.</summary>
    Task<string> ReadTextAsync(string documentId);
    Task<Document> WriteTextAsync(string documentId, string text, string expectedETag);

    Task<Link> AddLinkAsync(Link link);
    /// <summary>Saves the note.</summary>
    Task<Link> UpdateLinkAsync(Link link, string expectedETag);
    Task DeleteLinkAsync(string linkId, string expectedETag);
}
