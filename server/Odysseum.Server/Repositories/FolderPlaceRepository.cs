using Odysseum.Abstractions.Exceptions;
using Odysseum.Server.Models;

namespace Odysseum.Server.Repositories;

public sealed class FolderPlaceRepository(IStorageContext storage) : CachedRepository<FolderPlace>(storage), IFolderPlaceRepository
{
    public async Task<string> GetPathByFolderIdAsync(string folderId) => (await GetPlaceByFolderIdAsync(folderId)).Path;

    public async Task<FolderPlace> GetPlaceByFolderIdAsync(string folderId) => await GetByIdAsync(folderId)
        ?? throw new WorkspaceException(WorkspaceError.NotFound, "The folder no longer exists.");

    public async Task<FolderPlace> AddAsync(FolderPlace item) => ChangedItem(await Storage.AddFolderPlaceAsync(item), item.FolderId);

    /// <summary>Moves the folder, with everything in it, to the new path.</summary>
    public async Task<FolderPlace> UpdateAsync(FolderPlace item, string expectedETag) =>
        ChangedItem(await Storage.UpdateFolderPlaceAsync(item, expectedETag), item.FolderId);

    public async Task DeleteAsync(string id, string expectedETag) =>
        await Storage.DeleteFolderPlaceAsync(await GetPlaceByFolderIdAsync(id), expectedETag);

    protected override string IdOf(FolderPlace item) => item.FolderId;
    protected override string ProjectIdOf(FolderPlace item) => item.ProjectId;
    protected override string VersionOf(FolderPlace item) => item.ETag;
    protected override IReadOnlyList<FolderPlace> ChangedItemsIn(StorageChanges changes) => changes.FolderPlaces;
    protected override IReadOnlyList<string> RemovedIdsIn(StorageChanges changes) => changes.RemovedFolderPlaceIds;
}
