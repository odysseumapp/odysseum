using Odysseum.Abstractions.Folders;
using Odysseum.Abstractions.Projects;

namespace Odysseum.Abstractions.Documents;

public interface IDocument
{
    string Id { get; }
    IProject Project { get; }
    IFolder Folder { get; }
    DocumentKind Kind { get; }
    string Title { get; }
    string Synopsis { get; }
    string Notes { get; }
    DocumentStatus Status { get; }
    int WordGoal { get; }
    IReadOnlyList<IDocument> Links { get; }
    string? LinkNote(IDocument other);
    string Body { get; }
    string Revision { get; }
    DateTimeOffset Modified { get; }
}
