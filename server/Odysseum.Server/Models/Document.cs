using Odysseum.Abstractions.Documents;

namespace Odysseum.Server.Models;

public sealed record Document(string Id, string ProjectId, string FolderId, string Name, DocumentKind Kind, bool IsFolderDocument,
    string Title, string Synopsis, string Notes, DocumentStatus Status, int WordGoal, int WordCount, DateTimeOffset LastModified,
    string ETag) : IDocument;
