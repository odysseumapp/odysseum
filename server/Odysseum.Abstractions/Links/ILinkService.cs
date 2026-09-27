using Odysseum.Abstractions.Links.Events;

namespace Odysseum.Abstractions.Links;

public interface ILinkService
{
    event EventHandler<LinkEventArgs>? LinkCreated;
    event EventHandler<LinkEventArgs>? LinkUpdated;
    event EventHandler<LinkEventArgs>? LinkRemoved;

    Task<ILink> GetLinkByIdAsync(string linkId);
    /// <summary>The links whose two documents both exist.</summary>
    Task<IReadOnlyList<ILink>> GetLinksByProjectIdAsync(string projectId);
    Task<IReadOnlyList<ILink>> GetLinksByDocumentIdAsync(string documentId);
    /// <summary>Links two documents of the same project. A second link between the same two documents is refused.</summary>
    Task<ILink> CreateLinkAsync(string firstDocumentId, string secondDocumentId, string note = "");
    Task<ILink> UpdateLinkNoteAsync(string linkId, string note, string expectedETag);
    Task DeleteLinkAsync(string linkId, string expectedETag);
}
