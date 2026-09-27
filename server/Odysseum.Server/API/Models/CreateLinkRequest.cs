namespace Odysseum.Server.API.Models;

public record CreateLinkRequest(string FirstDocumentId, string SecondDocumentId, string? Note);
