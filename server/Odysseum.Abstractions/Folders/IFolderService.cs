using Odysseum.Abstractions.Folders.Events;
using Odysseum.Abstractions.Projects;

namespace Odysseum.Abstractions.Folders;

public interface IFolderService
{
    event EventHandler<FolderEventArgs>? FolderCreated;
    event EventHandler<FolderEventArgs>? FolderUpdated;
    event EventHandler<FolderEventArgs>? FolderRemoved;

    Task<IReadOnlyList<IFolder>> ListAsync(IProject project);
    Task<IFolder> GetAsync(IProject project, string id);
    Task<IFolder> CreateAsync(ProjectBranch branch, string parentId, string name);
    Task<IFolder> SetLayoutAsync(ProjectBranch branch, string folderId, FolderLayout layout, string expectedRevision);
    /// <summary>Puts a folder or document at <c>index</c> among the target folder's children. This is the only way
    /// to change the order. Returns the target folder.</summary>
    Task<IFolder> MoveAsync(ProjectBranch branch, string itemId, string targetFolderId, int index, string expectedRevision);
    Task RemoveAsync(ProjectBranch branch, string folderId, string expectedRevision);
}
