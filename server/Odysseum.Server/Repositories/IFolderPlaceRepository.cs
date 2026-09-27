using Odysseum.Server.Models;

namespace Odysseum.Server.Repositories;

/// <summary>Where each folder is stored. Adding a place makes an empty folder; updating it moves the folder with
/// everything in it; deleting it deletes the empty folder.</summary>
public interface IFolderPlaceRepository : IRepository<FolderPlace>
{
    /// <summary>The folder's path relative to the project. Throws when the folder does not exist.</summary>
    Task<string> GetPathByFolderIdAsync(string folderId);
    /// <summary>The folder's place. Throws when the folder does not exist.</summary>
    Task<FolderPlace> GetPlaceByFolderIdAsync(string folderId);
}
