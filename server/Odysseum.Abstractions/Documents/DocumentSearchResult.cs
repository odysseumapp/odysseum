namespace Odysseum.Abstractions.Documents;

/// <summary>A document that matches a search, with a part of its text near the match.</summary>
public sealed record DocumentSearchResult(IDocument Document, string Excerpt);
