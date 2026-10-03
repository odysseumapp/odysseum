using System.Collections.Concurrent;
using Odysseum.Abstractions.Changes;
using Odysseum.Abstractions.Exceptions;
using Odysseum.Server.Models;

namespace Odysseum.Server.Repositories;

/// <summary>Keeps every project, folder, document and link in memory. It fills the memory from
/// <see cref="IStorageContext.Changed"/>, which reports each write and each read of a project, also reads caused by
/// other programs changing the files. Reads never go to the storage. Document text is not kept.</summary>
public sealed class WorkspaceRepository : IWorkspaceRepository
{
    private readonly IStorageContext _storage;
    private readonly ConcurrentDictionary<string, Project> _projects = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Folder> _folders = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Document> _documents = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Link> _links = new(StringComparer.Ordinal);

    public WorkspaceRepository(IStorageContext storage)
    {
        _storage = storage;
        storage.Changed += Apply;
    }

    public event EventHandler<ChangesEventArgs>? Changed;

    public Task<T?> GetAsync<T>(string id) where T : class => Task.FromResult(Items<T>().GetValueOrDefault(id));

    public Task<IReadOnlyList<T>> GetAllAsync<T>(string projectId) where T : class =>
        Task.FromResult<IReadOnlyList<T>>(Items<T>().Values.Where(item => KeyOf(item).ProjectId == projectId).ToArray());

    public Task<IReadOnlyList<Project>> GetProjectsAsync() => Task.FromResult<IReadOnlyList<Project>>(_projects.Values.ToArray());

    public Task<IReadOnlyList<Link>> GetLinksForDocumentAsync(string documentId) =>
        Task.FromResult<IReadOnlyList<Link>>(_links.Values.Where(link => link.Joins(documentId)).ToArray());

    public async Task<Project> AddProjectAsync(string title, int wordGoal, int defaultSceneWordGoal) =>
        (await _storage.AddProjectAsync(title, wordGoal, defaultSceneWordGoal)).Projects.Single();

    public async Task<Project> UpdateProjectAsync(Project project, string expectedETag) =>
        Find((await _storage.UpdateProjectAsync(project, expectedETag)).Projects, project.Id);

    public Task FindNewProjectsAsync() => _storage.LoadNewProjectsAsync();

    public Task ReloadProjectAsync(string projectId) => _storage.ReloadProjectAsync(projectId);

    public async Task<Folder> AddFolderAsync(Folder folder) => Find((await _storage.AddFolderAsync(folder)).Folders, folder.Id);

    public async Task<Folder> UpdateFolderAsync(Folder folder, string expectedETag) =>
        Find((await _storage.UpdateFolderAsync(folder, expectedETag)).Folders, folder.Id);

    public async Task<Folder> MoveFolderAsync(string folderId, string targetParentId, int index, string expectedETag) =>
        Find((await _storage.MoveFolderAsync(RequireFolder(folderId), targetParentId, index, expectedETag)).Folders, folderId);

    public async Task DeleteFolderAsync(string folderId, string expectedETag) =>
        await _storage.DeleteFolderAsync(RequireFolder(folderId), expectedETag);

    public async Task<Document> AddDocumentAsync(Document document, string text) =>
        Find((await _storage.AddDocumentAsync(document, text)).Documents, document.Id);

    public async Task<Document> UpdateDocumentAsync(Document document, string expectedETag) =>
        Find((await _storage.UpdateDocumentAsync(document, expectedETag)).Documents, document.Id);

    public async Task<Document> MoveDocumentAsync(string documentId, string targetFolderId, int index, string expectedETag) =>
        Find((await _storage.MoveDocumentAsync(RequireDocument(documentId), targetFolderId, index, expectedETag)).Documents, documentId);

    public async Task DeleteDocumentAsync(string documentId, string expectedETag) =>
        await _storage.DeleteDocumentAsync(RequireDocument(documentId), expectedETag);

    public async Task<string> ReadTextAsync(string documentId) => await _storage.ReadDocumentTextAsync(RequireDocument(documentId));

    public async Task<Document> WriteTextAsync(string documentId, string text, string expectedETag) =>
        Find((await _storage.WriteDocumentTextAsync(RequireDocument(documentId), text, expectedETag)).Documents, documentId);

    public async Task<Link> AddLinkAsync(Link link) => Find((await _storage.AddLinkAsync(link)).Links, link.Id);

    public async Task<Link> UpdateLinkAsync(Link link, string expectedETag) => Find((await _storage.UpdateLinkAsync(link, expectedETag)).Links, link.Id);

    public async Task DeleteLinkAsync(string linkId, string expectedETag) =>
        await _storage.DeleteLinkAsync(Require(_links, linkId, "The link no longer exists."), expectedETag);

    private void Apply(StorageChanges stored)
    {
        var changes = new List<Change>();
        Apply(_projects, ItemType.Project, stored, stored.Projects, stored.RemovedProjectIds, changes);
        Apply(_folders, ItemType.Folder, stored, stored.Folders, stored.RemovedFolderIds, changes);
        Apply(_documents, ItemType.Document, stored, stored.Documents, stored.RemovedDocumentIds, changes);
        Apply(_links, ItemType.Link, stored, stored.Links, stored.RemovedLinkIds, changes);
        if (changes.Count > 0) Changed?.Invoke(this, new ChangesEventArgs(changes));
    }

    /// <summary>Puts the items in memory, and adds a change for each item that is new, has a new ETag, or is gone. An
    /// item is <see cref="ChangeKind.Moved"/> only when the storage's move wrote it.</summary>
    private static void Apply<T>(ConcurrentDictionary<string, T> items, ItemType type, StorageChanges stored, List<T> changed,
        List<string> removedIds, List<Change> changes) where T : class
    {
        var removed = new HashSet<string>(removedIds, StringComparer.Ordinal);
        if (stored.ReplacesProject)
        {
            var kept = changed.Select(item => KeyOf(item).Id).ToHashSet(StringComparer.Ordinal);
            removed.UnionWith(items.Values.Select(KeyOf).Where(key => key.ProjectId == stored.ProjectId && !kept.Contains(key.Id)).Select(key => key.Id));
        }
        foreach (var item in changed)
        {
            var (id, _, etag) = KeyOf(item);
            var before = items.GetValueOrDefault(id);
            items[id] = item;
            if (before is not null && KeyOf(before).ETag == etag) continue;
            var kind = before is null ? ChangeKind.Added : stored.MovedIds.Contains(id) ? ChangeKind.Moved : ChangeKind.Updated;
            changes.Add(new Change(kind, type, stored.ProjectId, id, etag));
        }
        foreach (var id in removed)
            if (items.TryRemove(id, out _)) changes.Add(new Change(ChangeKind.Removed, type, stored.ProjectId, id, null));
    }

    private ConcurrentDictionary<string, T> Items<T>()
    {
        object items = typeof(T) == typeof(Project) ? _projects : typeof(T) == typeof(Folder) ? _folders
            : typeof(T) == typeof(Document) ? _documents : typeof(T) == typeof(Link) ? _links
            : throw new NotSupportedException($"The workspace keeps no {typeof(T).Name} items.");
        return (ConcurrentDictionary<string, T>)items;
    }

    private static (string Id, string ProjectId, string ETag) KeyOf(object item) => item switch
    {
        Project project => (project.Id, project.Id, project.ETag),
        Folder folder => (folder.Id, folder.ProjectId, folder.ETag),
        Document document => (document.Id, document.ProjectId, document.ETag),
        Link link => (link.Id, link.ProjectId, link.ETag),
        _ => throw new NotSupportedException($"The workspace keeps no {item.GetType().Name} items."),
    };

    /// <summary>The item with that ID among the items a write returned.</summary>
    private static T Find<T>(List<T> items, string id) where T : class => items.First(item => KeyOf(item).Id == id);

    private Folder RequireFolder(string folderId) => Require(_folders, folderId, "The folder no longer exists.");

    private Document RequireDocument(string documentId) =>
        Require(_documents, documentId, "This document was removed or moved outside the workspace.");

    private static T Require<T>(ConcurrentDictionary<string, T> items, string id, string message) where T : class =>
        items.GetValueOrDefault(id) ?? throw new WorkspaceException(WorkspaceError.NotFound, message);
}
