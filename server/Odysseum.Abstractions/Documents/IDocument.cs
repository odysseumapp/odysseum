namespace Odysseum.Abstractions.Documents;

/// <summary>A document's details. The text is read separately through <see cref="IDocumentService.GetDocumentTextByIdAsync"/>.</summary>
public interface IDocument
{
    string Id { get; }
    string ProjectId { get; }
    string FolderId { get; }
    /// <summary>The file name with its extension.</summary>
    string Name { get; }
    /// <summary>Set from the project's top-level folder the document is in.</summary>
    DocumentKind Kind { get; }
    /// <summary>True for a folder's own hidden document.</summary>
    bool IsFolderDocument { get; }
    string Title { get; }
    string Synopsis { get; }
    string Notes { get; }
    DocumentStatus Status { get; }
    int WordGoal { get; }
    int WordCount { get; }
    DateTimeOffset LastModified { get; }
    /// <summary>Changes when the text, the details, the name or the place of the document change.</summary>
    string ETag { get; }
}
