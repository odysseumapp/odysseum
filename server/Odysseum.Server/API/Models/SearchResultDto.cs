namespace Odysseum.Server.API.Models;

/// <summary>A document that matches a search. <c>Excerpt</c> is up to 180 characters of its text near the match.</summary>
public record SearchResultDto(string DocumentId, string Title, string Excerpt);
