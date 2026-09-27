using Odysseum.Abstractions.Documents;

namespace Odysseum.Server.API.Models;

public record DocumentMoveDto(DocumentDto Document, FolderDto OldFolder, FolderDto NewFolder)
{
    public static DocumentMoveDto FromMoveResult(DocumentMoveResult result) => new(DocumentDto.FromDocument(result.Document),
        FolderDto.FromFolder(result.OldFolder), FolderDto.FromFolder(result.NewFolder));
}
