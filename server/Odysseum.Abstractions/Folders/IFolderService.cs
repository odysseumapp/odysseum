using Odysseum.Abstractions.Folders.Events;

namespace Odysseum.Abstractions.Folders;

public interface IFolderService
{
    event EventHandler<FolderEventArgs>? FolderCreated;
    event EventHandler<FolderEventArgs>? FolderUpdated;
    event EventHandler<FolderEventArgs>? FolderMoved;
    event EventHandler<FolderEventArgs>? FolderRemoved;

    Task<IFolder> GetFolderByIdAsync(string folderId);
    Task<IReadOnlyList<IFolder>> GetFoldersByProjectIdAsync(string projectId);
    /// <summary>Makes an empty folder at the end of the parent folder's children.</summary>
    Task<IFolder> CreateFolderAsync(string parentFolderId, string name);
    Task<IFolder> UpdateFolderLayoutAsync(string folderId, FolderLayout layout, string expectedETag);
    /// <summary>Puts the folder at <c>index</c> among the target folder's children. The index is clamped.</summary>
    Task<FolderMoveResult> MoveFolderToFolderAsync(string folderId, string targetFolderId, int index, string expectedETag);
    /// <summary>Deletes a folder that has no children. View settings in other folders that name it are cleared.</summary>
    Task DeleteFolderAsync(string folderId, string expectedETag);
}
