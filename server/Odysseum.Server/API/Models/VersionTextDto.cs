namespace Odysseum.Server.API.Models;

/// <summary>A document's text as it was in a saved version.</summary>
public record VersionTextDto(string VersionId, string DocumentId, string Text);
