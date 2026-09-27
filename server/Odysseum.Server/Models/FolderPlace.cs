namespace Odysseum.Server.Models;

/// <summary>Where a folder is stored. <c>Path</c> is relative to the project. The project's top folder has the empty path.
/// A change to the path moves the folder with everything in it.</summary>
public sealed record FolderPlace(string FolderId, string ProjectId, string Path, string ETag);
