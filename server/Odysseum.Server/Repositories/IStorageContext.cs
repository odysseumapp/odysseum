using Odysseum.Server.Models;

namespace Odysseum.Server.Repositories;

/// <summary>Reads and writes the stored data of all projects. The repositories are the only callers. It keeps no copy
/// of the data: each write reads what is stored now, checks the ETag the caller gave, and writes.
/// <see cref="Changed"/> reports every write and every read, so the repositories can update what they keep in memory.
/// Another storage (for example a database) implements this interface; the repositories do not change.</summary>
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

    /// <summary>Makes the folder's place: an empty folder at the path.</summary>
    Task<StorageChanges> AddFolderPlaceAsync(FolderPlace place);
    /// <summary>Moves the folder, with everything in it, to the new path.</summary>
    Task<StorageChanges> UpdateFolderPlaceAsync(FolderPlace place, string expectedETag);
    /// <summary>Deletes the folder's place. The folder must have no files other than its settings and its own document.</summary>
    Task<StorageChanges> DeleteFolderPlaceAsync(FolderPlace place, string expectedETag);

    /// <summary>Saves a new folder's layout and order. Its place must exist.</summary>
    Task<StorageChanges> AddFolderAsync(Folder folder, FolderPlace place);
    /// <summary>Saves the pinned view, the view settings and the order of the children. Other changes are not saved.</summary>
    Task<StorageChanges> UpdateFolderAsync(Folder folder, FolderPlace place, string expectedETag);
    Task<StorageChanges> DeleteFolderAsync(Folder folder, FolderPlace place, string expectedETag);

    /// <summary>Makes the document's place: an empty file at the path.</summary>
    Task<StorageChanges> AddDocumentPlaceAsync(DocumentPlace place);
    /// <summary>Moves or renames the document's file to the new path. The document's details go with it.</summary>
    Task<StorageChanges> UpdateDocumentPlaceAsync(DocumentPlace place, string expectedETag);
    /// <summary>Deletes the document's file.</summary>
    Task<StorageChanges> DeleteDocumentPlaceAsync(DocumentPlace place, string expectedETag);

    /// <summary>Saves a new document's details and text. Its place must exist.</summary>
    Task<StorageChanges> AddDocumentAsync(Document document, DocumentPlace place, string text);
    /// <summary>Saves the details. Other changes to the document are not saved.</summary>
    Task<StorageChanges> UpdateDocumentAsync(Document document, DocumentPlace place, string expectedETag);
    Task<StorageChanges> DeleteDocumentAsync(Document document, DocumentPlace place, string expectedETag);
    Task<string> ReadDocumentTextAsync(DocumentPlace place);
    Task<StorageChanges> WriteDocumentTextAsync(DocumentPlace place, string text, string expectedETag);

    Task<StorageChanges> AddLinkAsync(Link link);
    /// <summary>Saves the note. Other changes to the link are not saved.</summary>
    Task<StorageChanges> UpdateLinkAsync(Link link, string expectedETag);
    Task<StorageChanges> DeleteLinkAsync(Link link, string expectedETag);
}
