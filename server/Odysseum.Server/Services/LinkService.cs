using Odysseum.Abstractions.Exceptions;
using Odysseum.Abstractions.Links;
using Odysseum.Abstractions.Links.Events;
using Odysseum.Server.Models;
using Odysseum.Server.Repositories;

namespace Odysseum.Server.Services;

public sealed class LinkService : ILinkService
{
    private const int MaxLinksPerDocument = 200;
    private const int MaxNoteLength = 2000;
    private readonly ILinkRepository _links;
    private readonly IDocumentRepository _documents;

    public LinkService(ILinkRepository links, IDocumentRepository documents)
    {
        _links = links;
        _documents = documents;
        links.ItemAdded += (_, change) => LinkCreated?.Invoke(this, new(change.Item));
        links.ItemUpdated += (_, change) => LinkUpdated?.Invoke(this, new(change.Item));
        links.ItemRemoved += (_, change) => LinkRemoved?.Invoke(this, new(change.Item));
    }

    public event EventHandler<LinkEventArgs>? LinkCreated;
    public event EventHandler<LinkEventArgs>? LinkUpdated;
    public event EventHandler<LinkEventArgs>? LinkRemoved;

    public async Task<ILink> GetLinkByIdAsync(string linkId) => await FindAsync(linkId);

    public async Task<IReadOnlyList<ILink>> GetLinksByProjectIdAsync(string projectId) => await _links.GetLinksByProjectIdAsync(projectId);

    public async Task<IReadOnlyList<ILink>> GetLinksByDocumentIdAsync(string documentId)
    {
        await FindDocumentAsync(documentId);
        return await _links.GetLinksByDocumentIdAsync(documentId);
    }

    public async Task<ILink> CreateLinkAsync(string firstDocumentId, string secondDocumentId, string note = "")
    {
        var first = await FindDocumentAsync(firstDocumentId);
        var second = await FindDocumentAsync(secondDocumentId);
        if (first.Id == second.Id) throw new WorkspaceException(WorkspaceError.Invalid, "A document cannot be linked to itself.");
        if (first.ProjectId != second.ProjectId) throw new WorkspaceException(WorkspaceError.Invalid, "Only documents of the same project can be linked.");
        if ((await _links.GetLinksByDocumentIdAsync(first.Id)).Count >= MaxLinksPerDocument
            || (await _links.GetLinksByDocumentIdAsync(second.Id)).Count >= MaxLinksPerDocument)
            throw new WorkspaceException(WorkspaceError.Invalid, "A document can have at most 200 links.");
        return await _links.AddAsync(new Link(Guid.NewGuid().ToString(), first.ProjectId, first.Id, second.Id, CheckNote(note), ""));
    }

    public async Task<ILink> UpdateLinkNoteAsync(string linkId, string note, string expectedETag)
    {
        var link = await FindAsync(linkId);
        return await _links.UpdateAsync(link with { Note = CheckNote(note) }, expectedETag);
    }

    public async Task DeleteLinkAsync(string linkId, string expectedETag)
    {
        await FindAsync(linkId);
        await _links.DeleteAsync(linkId, expectedETag);
    }

    private static string CheckNote(string? note)
    {
        note = note?.Trim() ?? "";
        return note.Length <= MaxNoteLength ? note : throw new WorkspaceException(WorkspaceError.Invalid, "A link note is too long.");
    }

    private async Task<Link> FindAsync(string linkId) => await _links.GetByIdAsync(linkId)
        ?? throw new WorkspaceException(WorkspaceError.NotFound, "The link no longer exists.");

    private async Task<Document> FindDocumentAsync(string documentId) => await _documents.GetByIdAsync(documentId)
        ?? throw new WorkspaceException(WorkspaceError.NotFound, "This document was removed or moved outside the workspace.");
}
