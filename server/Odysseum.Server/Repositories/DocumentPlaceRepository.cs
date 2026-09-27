using Odysseum.Abstractions.Exceptions;
using Odysseum.Server.Models;

namespace Odysseum.Server.Repositories;

public sealed class DocumentPlaceRepository(IStorageContext storage) : CachedRepository<DocumentPlace>(storage), IDocumentPlaceRepository
{
    public async Task<string> GetPathByDocumentIdAsync(string documentId) => (await GetPlaceByDocumentIdAsync(documentId)).Path;

    public async Task<DocumentPlace> GetPlaceByDocumentIdAsync(string documentId) => await GetByIdAsync(documentId)
        ?? throw new WorkspaceException(WorkspaceError.NotFound, "This document was removed or moved outside the workspace.");

    public Task<IReadOnlyList<DocumentPlace>> GetPlacesInFolderAsync(string projectId, string folderPath) =>
        Task.FromResult<IReadOnlyList<DocumentPlace>>(Query().Where(place => place.ProjectId == projectId && ProjectPaths.ParentOf(place.Path) == folderPath).ToArray());

    public async Task<DocumentPlace> AddAsync(DocumentPlace item) => ChangedItem(await Storage.AddDocumentPlaceAsync(item), item.DocumentId);

    /// <summary>Moves or renames the document's file to the new path.</summary>
    public async Task<DocumentPlace> UpdateAsync(DocumentPlace item, string expectedETag) =>
        ChangedItem(await Storage.UpdateDocumentPlaceAsync(item, expectedETag), item.DocumentId);

    public async Task DeleteAsync(string id, string expectedETag) =>
        await Storage.DeleteDocumentPlaceAsync(await GetPlaceByDocumentIdAsync(id), expectedETag);

    protected override string IdOf(DocumentPlace item) => item.DocumentId;
    protected override string ProjectIdOf(DocumentPlace item) => item.ProjectId;
    protected override string VersionOf(DocumentPlace item) => item.ETag;
    protected override IReadOnlyList<DocumentPlace> ChangedItemsIn(StorageChanges changes) => changes.DocumentPlaces;
    protected override IReadOnlyList<string> RemovedIdsIn(StorageChanges changes) => changes.RemovedDocumentPlaceIds;
}
