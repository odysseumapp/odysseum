namespace Odysseum.Server.API.Models;

/// <summary>The new file name comes from <c>Name</c> and keeps the document's extension.</summary>
public record RenameDocumentRequest(string Name);
