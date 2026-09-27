using Odysseum.Server.Models;

namespace Odysseum.Server.Repositories;

public sealed class FolderRepository(IStorageContext storage, IFolderPlaceRepository places)
    : CachedRepository<Folder>(storage), IFolderRepository
{
    public Task<IReadOnlyList<Folder>> GetFoldersByProjectIdAsync(string projectId) =>
        Task.FromResult<IReadOnlyList<Folder>>(Query().Where(folder => folder.ProjectId == projectId).ToArray());

    /// <summary>Saves a new folder's layout. The folder's place must exist.</summary>
    public async Task<Folder> AddAsync(Folder item) =>
        ChangedItem(await Storage.AddFolderAsync(item, await places.GetPlaceByFolderIdAsync(item.Id)), item.Id);

    /// <summary>Saves the pinned view, the view settings and the order of the children.</summary>
    public async Task<Folder> UpdateAsync(Folder item, string expectedETag) =>
        ChangedItem(await Storage.UpdateFolderAsync(item, await places.GetPlaceByFolderIdAsync(item.Id), expectedETag), item.Id);

    public async Task DeleteAsync(string id, string expectedETag)
    {
        var folder = await GetByIdAsync(id) ?? throw new Abstractions.Exceptions.WorkspaceException(
            Abstractions.Exceptions.WorkspaceError.NotFound, "The folder no longer exists.");
        await Storage.DeleteFolderAsync(folder, await places.GetPlaceByFolderIdAsync(id), expectedETag);
    }

    protected override string IdOf(Folder item) => item.Id;
    protected override string ProjectIdOf(Folder item) => item.ProjectId;
    protected override string VersionOf(Folder item) => item.ETag;
    protected override IReadOnlyList<Folder> ChangedItemsIn(StorageChanges changes) => changes.Folders;
    protected override IReadOnlyList<string> RemovedIdsIn(StorageChanges changes) => changes.RemovedFolderIds;
}
