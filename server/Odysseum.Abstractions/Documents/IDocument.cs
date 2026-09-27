using Odysseum.Abstractions.Items;

namespace Odysseum.Abstractions.Documents;

public interface IDocument : IItem
{
    DocumentKind Kind { get; }
    bool IsFolderDocument { get; }
    string Title { get; }
    string Synopsis { get; }
    string Notes { get; }
    DocumentStatus Status { get; }
    int WordGoal { get; }
    /// <summary>IDs of the documents this one is linked to. Links are undirected.</summary>
    IReadOnlyList<string> Links { get; }
    /// <summary>One note per link, keyed by the linked document's ID. Both ends of a link share the note.</summary>
    IReadOnlyDictionary<string, string> LinkNotes { get; }
    /// <summary>The SHA-256 of the file on disk.</summary>
    string Revision { get; }
    DateTimeOffset Modified { get; }
    int WordCount { get; }
    /// <summary>The prose. Null until the document is opened through the document service.</summary>
    string? Body { get; }
}
