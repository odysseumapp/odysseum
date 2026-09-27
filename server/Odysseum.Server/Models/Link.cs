using Odysseum.Abstractions.Links;

namespace Odysseum.Server.Models;

public sealed record Link(string Id, string ProjectId, string FirstDocumentId, string SecondDocumentId, string Note, string ETag) : ILink
{
    public bool Joins(string documentId) => FirstDocumentId == documentId || SecondDocumentId == documentId;

    public bool Joins(string first, string second) =>
        (FirstDocumentId == first && SecondDocumentId == second) || (FirstDocumentId == second && SecondDocumentId == first);
}
