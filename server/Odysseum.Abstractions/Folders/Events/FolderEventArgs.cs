namespace Odysseum.Abstractions.Folders.Events;

public sealed class FolderEventArgs(IFolder folder) : EventArgs
{
    public IFolder Folder { get; } = folder;
}
