using Odysseum.Server.Models;

namespace Odysseum.Server.Repositories;

/// <summary>Reads and writes the stored data of all projects. The workspace repository is the only caller. It keeps no
/// copy of the data: each write reads what is stored now, checks the ETag the caller gave, and writes.
/// <see cref="Changed"/> reports every write and every read, so the repository can update what it keeps in memory.
/// Another storage (for example a database) implements this interface; the repository does not change.
/// A new folder or document goes where its folder (<c>ParentFolderId</c> or <c>FolderId</c>) and its <c>Name</c> say;
/// only a move puts it in another folder. The storage sets <c>Path</c>; the caller's <c>Path</c> is not read.</summary>
public interface IStorageContext : IProjectLock
{
    /// <summary>Raised after each write and each read of a project, inside the project lock.</summary>
    event Action<StorageChanges>? Changed;

    /// <summary>Reads every project. The server calls it once at startup.</summary>
    Task LoadAllProjectsAsync();
    /// <summary>Reads the projects that were added since the last read, and reports the ones that are gone.</summary>
    Task LoadNewProjectsAsync();
    /// <summary>Reads the project again, for example after its files were restored from a version.</summary>
    Task ReloadProjectAsync(string projectId);

    /// <summary>Makes an empty project with a new ID. Its name comes from the title.</summary>
    Task<StorageChanges> AddProjectAsync(string title, int wordGoal, int defaultSceneWordGoal);
    /// <summary>Saves the title and the word goals.</summary>
    Task<StorageChanges> UpdateProjectAsync(Project project, string expectedETag);

    /// <summary>Makes the folder with its settings and its own hidden document, at the end of the parent folder's children.</summary>
    Task<StorageChanges> AddFolderAsync(Folder folder);
    /// <summary>Saves the pinned view, the view settings and the order of the children. Other changes are not saved.</summary>
    Task<StorageChanges> UpdateFolderAsync(Folder folder, string expectedETag);
    /// <summary>Moves the folder, with everything in it, to <paramref name="index"/> among the target folder's children,
    /// and saves the new order. The index is clamped. A move inside the same folder only changes the order.</summary>
    Task<StorageChanges> MoveFolderAsync(Folder folder, string targetParentId, int index, string expectedETag);
    /// <summary>Deletes a folder that has no children, with its settings and its own document.</summary>
    Task<StorageChanges> DeleteFolderAsync(Folder folder, string expectedETag);

    /// <summary>Makes the document's file and saves its details, at the end of the folder's children.</summary>
    Task<StorageChanges> AddDocumentAsync(Document document, string text);
    /// <summary>Saves the details, and renames the file when <c>Name</c> changed. Other changes are not saved.</summary>
    Task<StorageChanges> UpdateDocumentAsync(Document document, string expectedETag);
    /// <summary>Moves the file, with the details, to <paramref name="index"/> among the target folder's children, and saves
    /// the new order. The index is clamped. A move inside the same folder only changes the order.</summary>
    Task<StorageChanges> MoveDocumentAsync(Document document, string targetFolderId, int index, string expectedETag);
    /// <summary>Deletes the file and the details. A folder's own document goes only with its folder.</summary>
    Task<StorageChanges> DeleteDocumentAsync(Document document, string expectedETag);
    Task<string> ReadDocumentTextAsync(Document document);
    Task<StorageChanges> WriteDocumentTextAsync(Document document, string text, string expectedETag);

    Task<StorageChanges> AddLinkAsync(Link link);
    /// <summary>Saves the note. Other changes to the link are not saved.</summary>
    Task<StorageChanges> UpdateLinkAsync(Link link, string expectedETag);
    Task<StorageChanges> DeleteLinkAsync(Link link, string expectedETag);
}
