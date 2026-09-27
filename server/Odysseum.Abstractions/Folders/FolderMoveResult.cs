namespace Odysseum.Abstractions.Folders;

/// <summary>A moved folder, with the folder it left and the folder it went to. The two are the same folder when the
/// move only changed the order.</summary>
public sealed record FolderMoveResult(IFolder Folder, IFolder OldParentFolder, IFolder NewParentFolder);
