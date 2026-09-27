using Odysseum.Abstractions.Exceptions;
using Odysseum.Server.Models;

namespace Odysseum.Server.Repositories;

public sealed class DocumentRepository(IStorageContext storage, IDocumentPlaceRepository places)
    : CachedRepository<Document>(storage), IDocumentRepository
{
    public Task<IReadOnlyList<Document>> GetDocumentsByProjectIdAsync(string projectId) =>
        Task.FromResult<IReadOnlyList<Document>>(Query().Where(document => document.ProjectId == projectId).ToArray());

    public Task<Document> AddAsync(Document item) => AddAsync(item, "");

    public async Task<Document> AddAsync(Document item, string text) =>
        ChangedItem(await Storage.AddDocumentAsync(item, await places.GetPlaceByDocumentIdAsync(item.Id), text), item.Id);

    /// <summary>Saves the details.</summary>
    public async Task<Document> UpdateAsync(Document item, string expectedETag) =>
        ChangedItem(await Storage.UpdateDocumentAsync(item, await places.GetPlaceByDocumentIdAsync(item.Id), expectedETag), item.Id);

    public async Task<string> GetTextByDocumentIdAsync(string documentId) =>
        await Storage.ReadDocumentTextAsync(await places.GetPlaceByDocumentIdAsync(documentId));

    public async Task<Document> UpdateTextAsync(string documentId, string text, string expectedETag) =>
        ChangedItem(await Storage.WriteDocumentTextAsync(await places.GetPlaceByDocumentIdAsync(documentId), text, expectedETag), documentId);

    /// <summary>Deletes the document's details. Its place, and with it the file, is deleted through the place repository.</summary>
    public async Task DeleteAsync(string id, string expectedETag)
    {
        var document = await GetByIdAsync(id) ?? throw new WorkspaceException(WorkspaceError.NotFound, "This document no longer exists.");
        await Storage.DeleteDocumentAsync(document, await places.GetPlaceByDocumentIdAsync(id), expectedETag);
    }

    protected override string IdOf(Document item) => item.Id;
    protected override string ProjectIdOf(Document item) => item.ProjectId;
    protected override string VersionOf(Document item) => item.ETag;
    protected override IReadOnlyList<Document> ChangedItemsIn(StorageChanges changes) => changes.Documents;
    protected override IReadOnlyList<string> RemovedIdsIn(StorageChanges changes) => changes.RemovedDocumentIds;
}
