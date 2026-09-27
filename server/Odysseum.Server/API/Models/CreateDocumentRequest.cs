namespace Odysseum.Server.API.Models;

/// <summary>Makes a document at the end of the folder's children. The file name comes from the title.</summary>
public record CreateDocumentRequest(string FolderId, string Title, string? Text);
