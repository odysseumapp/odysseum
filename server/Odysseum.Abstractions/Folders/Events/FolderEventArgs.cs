using Odysseum.Abstractions.Projects;

namespace Odysseum.Abstractions.Folders.Events;

public sealed class FolderEventArgs(ProjectBranch branch, IFolder folder) : EventArgs
{
    public ProjectBranch Branch { get; } = branch;
    public IFolder Folder { get; } = folder;
}
