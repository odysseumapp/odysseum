using Odysseum.Abstractions.Documents;

namespace Odysseum.Server.Models;

/// <summary>A document's details. <c>Path</c> is relative to the project, for example <c>Manuscript/Chapter 1.md</c>.
/// The storage sets the path from <c>FolderId</c> and <c>Name</c>.</summary>
public sealed record Document(string Id, string ProjectId, string FolderId, string Name, string Path, DocumentKind Kind, bool IsFolderDocument,
    string Title, string Synopsis, string Notes, DocumentStatus Status, int WordGoal, int WordCount, DateTimeOffset LastModified,
    string ETag) : IDocument;
