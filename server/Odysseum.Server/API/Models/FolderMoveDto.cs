using Odysseum.Abstractions.Folders;

namespace Odysseum.Server.API.Models;

public record FolderMoveDto(FolderDto Folder, FolderDto OldParentFolder, FolderDto NewParentFolder)
{
    public static FolderMoveDto FromMoveResult(FolderMoveResult result) => new(FolderDto.FromFolder(result.Folder),
        FolderDto.FromFolder(result.OldParentFolder), FolderDto.FromFolder(result.NewParentFolder));
}
