using Odysseum.Abstractions.Documents;
using Odysseum.Abstractions.Documents.Events;
using Odysseum.Abstractions.Exceptions;
using Odysseum.Server.Models;
using Odysseum.Server.Repositories;
using static Odysseum.Server.Services.Documents.DocumentRules;

namespace Odysseum.Server.Services;

public sealed class DocumentService : IDocumentService
{
    private const int MaxSearchResults = 50;
    private readonly IDocumentRepository _documents;
    private readonly IDocumentPlaceRepository _documentPlaces;
    private readonly IFolderRepository _folders;
    private readonly IFolderPlaceRepository _folderPlaces;
    private readonly IProjectRepository _projects;
    private readonly IProjectLock _projectLock;

    public DocumentService(IDocumentRepository documents, IDocumentPlaceRepository documentPlaces, IFolderRepository folders,
        IFolderPlaceRepository folderPlaces, IProjectRepository projects, IProjectLock projectLock)
    {
        _documents = documents;
        _documentPlaces = documentPlaces;
        _folders = folders;
        _folderPlaces = folderPlaces;
        _projects = projects;
        _projectLock = projectLock;
        documents.ItemAdded += (_, change) => DocumentCreated?.Invoke(this, new(change.Item));
        documents.ItemUpdated += (_, change) =>
        {
            if (change.Before?.FolderId != change.Item.FolderId || change.Before?.Kind != change.Item.Kind) DocumentMoved?.Invoke(this, new(change.Item));
            else DocumentUpdated?.Invoke(this, new(change.Item));
        };
        documents.ItemRemoved += (_, change) => DocumentRemoved?.Invoke(this, new(change.Item));
    }

    public event EventHandler<DocumentEventArgs>? DocumentCreated;
    public event EventHandler<DocumentEventArgs>? DocumentUpdated;
    public event EventHandler<DocumentEventArgs>? DocumentMoved;
    public event EventHandler<DocumentEventArgs>? DocumentRemoved;

    public async Task<IDocument> GetDocumentByIdAsync(string documentId) => await FindAsync(documentId);

    public async Task<IReadOnlyList<IDocument>> GetDocumentsByProjectIdAsync(string projectId) => await DocumentsInOrderAsync(projectId);

    public async Task<IReadOnlyList<IDocument>> GetDocumentsByFolderIdAsync(string folderId)
    {
        var folder = await FindFolderAsync(folderId);
        var documents = new List<IDocument>();
        foreach (var id in folder.ChildIds)
            if (await _documents.GetByIdAsync(id) is { } document) documents.Add(document);
        return documents;
    }

    public async Task<string> GetDocumentTextByIdAsync(string documentId)
    {
        await FindAsync(documentId);
        return await _documents.GetTextByDocumentIdAsync(documentId);
    }

    /// <summary>Saves the document's place, then the document, then adds it at the end of the folder's children.</summary>
    public async Task<IDocument> CreateDocumentAsync(string folderId, string title, string? text = null)
    {
        title = ValidateTitle(title);
        var folder = await FindFolderAsync(folderId);
        var project = await _projects.GetByIdAsync(folder.ProjectId)
            ?? throw new WorkspaceException(WorkspaceError.NotFound, "That project no longer exists in the workspace.");
        return await _projectLock.RunLockedAsync(folder.ProjectId, async () =>
        {
            var folderPath = await _folderPlaces.GetPathByFolderIdAsync(folderId);
            var name = await FreeFileNameAsync(folder.ProjectId, folderPath, FileName(title), ".md");
            var path = ProjectPaths.Join(folderPath, name);
            var id = Guid.NewGuid().ToString();
            await _documentPlaces.AddAsync(new DocumentPlace(id, folder.ProjectId, path, ""));
            var document = await _documents.AddAsync(new Document(id, folder.ProjectId, folder.Id, name, KindOf(path), false, title, "", "",
                DocumentStatus.Draft, project.DefaultSceneWordGoal, 0, default, ""), string.IsNullOrEmpty(text) ? StarterContent(path) : text);
            var current = await FindFolderAsync(folderId);
            await _folders.UpdateAsync(current with { ChildIds = [.. current.ChildIds.Where(child => child != id), id] }, current.ETag);
            return (IDocument)document;
        });
    }

    public async Task<IDocument> UpdateDocumentTextAsync(string documentId, string text, string expectedETag)
    {
        if (text is null) throw new WorkspaceException(WorkspaceError.Invalid, "Document text is required.");
        await FindAsync(documentId);
        return await _documents.UpdateTextAsync(documentId, text, expectedETag);
    }

    public async Task<IDocument> UpdateDocumentDetailsAsync(string documentId, DocumentDetails details, string expectedETag)
    {
        var document = await FindAsync(documentId);
        if (details.Synopsis is null || details.Notes is null) throw new WorkspaceException(WorkspaceError.Invalid, "Synopsis and notes are required.");
        if (!Enum.IsDefined(details.Status)) throw new WorkspaceException(WorkspaceError.Invalid, "Unknown document status.");
        if (details.WordGoal is < 0 or > 10000000) throw new WorkspaceException(WorkspaceError.Invalid, "Invalid word goal.");
        if (details.Synopsis.Length > 20000 || details.Notes.Length > 100000) throw new WorkspaceException(WorkspaceError.Invalid, "The synopsis or the notes are too long.");
        return await _documents.UpdateAsync(document with
        {
            Title = ValidateTitle(details.Title), Synopsis = details.Synopsis, Notes = details.Notes, Status = details.Status, WordGoal = details.WordGoal,
        }, expectedETag);
    }

    /// <summary>Changes the document's place to a new file name in the same folder.</summary>
    public async Task<IDocument> RenameDocumentAsync(string documentId, string name, string expectedETag)
    {
        var document = await FindAsync(documentId);
        if (document.IsFolderDocument) throw new WorkspaceException(WorkspaceError.Invalid, "A folder's own document keeps its name.");
        var fileName = FileName(ValidateTitle(name)) + Path.GetExtension(document.Name);
        return await _projectLock.RunLockedAsync(document.ProjectId, async () =>
        {
            ETags.Check((await FindAsync(documentId)).ETag, expectedETag);
            var place = await _documentPlaces.GetPlaceByDocumentIdAsync(documentId);
            await _documentPlaces.UpdateAsync(place with { Path = ProjectPaths.Join(ProjectPaths.ParentOf(place.Path), fileName) }, place.ETag);
            return (IDocument)await FindAsync(documentId);
        });
    }

    /// <summary>Changes the document's place to the target folder, then puts it at <paramref name="index"/> among the
    /// target folder's children.</summary>
    public async Task<DocumentMoveResult> MoveDocumentToFolderAsync(string documentId, string targetFolderId, int index, string expectedETag)
    {
        var document = await FindAsync(documentId);
        if (document.IsFolderDocument) throw new WorkspaceException(WorkspaceError.Invalid, "A folder's own document stays with its folder.");
        var target = await FindFolderAsync(targetFolderId);
        if (target.ProjectId != document.ProjectId) throw new WorkspaceException(WorkspaceError.Invalid, "A document can only move inside its own project.");
        return await _projectLock.RunLockedAsync(document.ProjectId, async () =>
        {
            document = await FindAsync(documentId);
            ETags.Check(document.ETag, expectedETag);
            var oldFolderId = document.FolderId;
            if (oldFolderId != targetFolderId)
            {
                var place = await _documentPlaces.GetPlaceByDocumentIdAsync(documentId);
                var targetPath = await _folderPlaces.GetPathByFolderIdAsync(targetFolderId);
                await _documentPlaces.UpdateAsync(place with { Path = ProjectPaths.Join(targetPath, document.Name) }, place.ETag);
            }
            target = await FindFolderAsync(targetFolderId);
            var order = target.ChildIds.Where(child => child != documentId).ToList();
            order.Insert(Math.Clamp(index, 0, order.Count), documentId);
            await _folders.UpdateAsync(target with { ChildIds = order }, target.ETag);
            return new DocumentMoveResult(await FindAsync(documentId), await FindFolderAsync(oldFolderId), await FindFolderAsync(targetFolderId));
        });
    }

    public async Task<IReadOnlyList<DocumentSearchResult>> SearchDocumentsAsync(string projectId, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var results = new List<DocumentSearchResult>();
        foreach (var document in await DocumentsInOrderAsync(projectId))
        {
            var body = await _documents.GetTextByDocumentIdAsync(document.Id);
            var position = body.IndexOf(text, StringComparison.OrdinalIgnoreCase);
            if (position < 0 && !document.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
                && !document.Synopsis.Contains(text, StringComparison.OrdinalIgnoreCase)
                && !document.Notes.Contains(text, StringComparison.OrdinalIgnoreCase)) continue;
            var start = Math.Max(0, position - 55);
            results.Add(new DocumentSearchResult(document, body.Substring(start, Math.Min(180, body.Length - start)).Replace('\n', ' ')));
            if (results.Count == MaxSearchResults) break;
        }
        return results;
    }

    /// <summary>Every document of the project in manuscript order: a folder's own document first, then its children in
    /// order, with each subfolder's documents in its place.</summary>
    private async Task<List<Document>> DocumentsInOrderAsync(string projectId)
    {
        if (await _projects.GetByIdAsync(projectId) is null) throw new WorkspaceException(WorkspaceError.NotFound, "No project has that ID.");
        var documents = new List<Document>();
        await AddInOrderAsync(projectId, documents);
        return documents;
    }

    private async Task AddInOrderAsync(string folderId, List<Document> documents)
    {
        if (await _folders.GetByIdAsync(folderId) is not { } folder) return;
        if (folder.OwnDocumentId is { } own && await _documents.GetByIdAsync(own) is { } ownDocument) documents.Add(ownDocument);
        foreach (var id in folder.ChildIds)
        {
            if (await _documents.GetByIdAsync(id) is { } document) documents.Add(document);
            else await AddInOrderAsync(id, documents);
        }
    }

    /// <summary>The file name, with a number added when another document in the folder already has it.</summary>
    private async Task<string> FreeFileNameAsync(string projectId, string folderPath, string stem, string extension)
    {
        var taken = (await _documentPlaces.GetPlacesInFolderAsync(projectId, folderPath))
            .Select(place => ProjectPaths.NameOf(place.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = stem + extension;
        for (var suffix = 2; taken.Contains(name); suffix++) name = $"{stem}-{suffix}{extension}";
        return name;
    }

    private async Task<Document> FindAsync(string documentId) => await _documents.GetByIdAsync(documentId)
        ?? throw new WorkspaceException(WorkspaceError.NotFound, "This document was removed or moved outside the workspace.");

    private async Task<Folder> FindFolderAsync(string folderId) => await _folders.GetByIdAsync(folderId)
        ?? throw new WorkspaceException(WorkspaceError.NotFound, "The folder no longer exists.");
}
