using Odysseum.Abstractions.Exceptions;
using Odysseum.Server.Models;

namespace Odysseum.Server.Repositories;

public sealed class LinkRepository(IStorageContext storage) : CachedRepository<Link>(storage), ILinkRepository
{
    public Task<IReadOnlyList<Link>> GetLinksByProjectIdAsync(string projectId) =>
        Task.FromResult<IReadOnlyList<Link>>(Query().Where(link => link.ProjectId == projectId).ToArray());

    public Task<IReadOnlyList<Link>> GetLinksByDocumentIdAsync(string documentId) =>
        Task.FromResult<IReadOnlyList<Link>>(Query().Where(link => link.Joins(documentId)).ToArray());

    public async Task<Link> AddAsync(Link item) => ChangedItem(await Storage.AddLinkAsync(item), item.Id);

    /// <summary>Saves the note.</summary>
    public async Task<Link> UpdateAsync(Link item, string expectedETag) =>
        ChangedItem(await Storage.UpdateLinkAsync(item, expectedETag), item.Id);

    public async Task DeleteAsync(string id, string expectedETag)
    {
        var link = await GetByIdAsync(id) ?? throw new WorkspaceException(WorkspaceError.NotFound, "The link no longer exists.");
        await Storage.DeleteLinkAsync(link, expectedETag);
    }

    protected override string IdOf(Link item) => item.Id;
    protected override string ProjectIdOf(Link item) => item.ProjectId;
    protected override string VersionOf(Link item) => item.ETag;
    protected override IReadOnlyList<Link> ChangedItemsIn(StorageChanges changes) => changes.Links;
    protected override IReadOnlyList<string> RemovedIdsIn(StorageChanges changes) => changes.RemovedLinkIds;
}
