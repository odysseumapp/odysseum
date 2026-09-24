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
    Task<IFolder> CreateAsync(IFolder parent, string name);
    Task<IFolder> SetLayoutAsync(IFolder folder, FolderLayout layout, string expectedRevision);
    Task RemoveAsync(IFolder folder, string expectedRevision);
}
