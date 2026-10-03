using Odysseum.Abstractions.Items;

namespace Odysseum.Abstractions.Documents;

/// <summary>A document's details. The text is read separately through <see cref="Projects.IProjectService.GetDocumentTextAsync"/>.
/// The ETag changes when the text, the details, the name or the place of the document change.</summary>
public interface IDocument : IProjectItem
{
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
}
