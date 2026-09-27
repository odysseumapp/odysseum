namespace Odysseum.Abstractions.Documents;

/// <summary>All details of a document. A save replaces all of them.</summary>
public sealed record DocumentDetails(string Title, string Synopsis, string Notes, DocumentStatus Status, int WordGoal);
