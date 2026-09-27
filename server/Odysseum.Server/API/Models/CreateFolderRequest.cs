namespace Odysseum.Server.API.Models;

public record CreateFolderRequest(string ParentFolderId, string Name);
