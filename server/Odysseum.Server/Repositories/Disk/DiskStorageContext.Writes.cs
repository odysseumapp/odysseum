using Odysseum.Abstractions.Exceptions;
using Odysseum.Server.Models;
using Odysseum.Server.Repositories.Disk.Formats;
using Odysseum.Server.Services.Documents;
using static Odysseum.Server.Services.Documents.MarkdownDocumentCodec;

namespace Odysseum.Server.Repositories.Disk;

public sealed partial class DiskStorageContext
{
    /// <summary><c>folders.json</c> and <c>documents.json</c> as read now.</summary>
    private sealed record PlacesFiles(PlacesFile Folders, byte[]? FolderBytes, PlacesFile Documents, byte[]? DocumentBytes)
    {
        public (string Path, object Content, byte[]? Before) FoldersToSave() => (SettingsFiles.FolderPlacesFilePath, Folders, FolderBytes);
        public (string Path, object Content, byte[]? Before) DocumentsToSave() => (SettingsFiles.DocumentPlacesFilePath, Documents, DocumentBytes);
    }

    /// <summary>A folder's settings file as read now, with the folder's children on the disk. The order in
    /// <c>File.ItemOrder</c> lists exactly the children.</summary>
    private sealed record FolderState(string Path, FolderFile File, byte[]? Bytes, string? ParentId, string? OwnDocumentId,
        IReadOnlyList<(string Id, string Name)> Subfolders, IReadOnlyList<(string Id, string Name)> Documents)
    {
        public string Id => File.Id;
        public (string Path, object Content, byte[]? Before) ToSave() => (SettingsFiles.PathOf(Path), File, Bytes);
    }

    public async Task<StorageChanges> AddProjectAsync(string title, int wordGoal, int defaultSceneWordGoal)
    {
        await _workspaceGate.WaitAsync();
        try
        {
            Directory.CreateDirectory(_workspace.Root);
            var stem = DocumentRules.FileName(title);
            var name = stem;
            for (var suffix = 2; _workspace.FolderExists(name) || _workspace.Exists(name); suffix++) name = $"{stem}-{suffix}";
            _workspace.CreateFolder(name);
            var files = new Files.FileManager(Path.Combine(_workspace.Root, name), _ownWrites);
            var file = new ProjectFile { Id = NewId(), Settings = new() { Title = title, WordGoal = wordGoal, DefaultSceneWordGoal = defaultSceneWordGoal } };
            await files.WriteAsync(SettingsFiles.ProjectFilePath, SettingsFiles.Serialize(file), overwrite: false, metadata: true);
            return await OpenAsync(name);
        }
        finally { _workspaceGate.Release(); }
    }

    public Task<StorageChanges> UpdateProjectAsync(Project project, string expectedETag)
    {
        var open = Get(project.Id);
        return RunLockedAsync(open.Id, async () =>
        {
            var (file, bytes) = await SettingsFiles.ReadProjectFileAsync(open.Files);
            if (file is null) throw new WorkspaceException(WorkspaceError.NotFound, "That project no longer exists in the workspace.");
            file.Id = open.Id;
            ETags.Check(ProjectModel(open, file).ETag, expectedETag);
            file.Settings = new() { Title = project.Title, WordGoal = project.WordGoal, DefaultSceneWordGoal = project.DefaultSceneWordGoal };
            await SaveFilesAsync(open.Files, (SettingsFiles.ProjectFilePath, file, bytes));
            open.LastModified = DateTimeOffset.UtcNow;
            var changes = new StorageChanges(open.Id);
            changes.Projects.Add(ProjectModel(open, file));
            return Publish(changes);
        });
    }

    public Task<StorageChanges> AddFolderPlaceAsync(FolderPlace place)
    {
        var open = Get(place.ProjectId);
        return RunLockedAsync(open.Id, async () =>
        {
            var files = open.Files;
            if (place.Path.Length == 0) throw new WorkspaceException(WorkspaceError.Invalid, "The project folder already exists.");
            var places = await ReadPlacesAsync(open);
            if (places.Folders.Paths.ContainsKey(place.FolderId)) throw new WorkspaceException(WorkspaceError.Conflict, "That folder already has a place.");
            var parent = await ReadFolderStateAsync(open, places, ProjectPaths.ParentOf(place.Path));
            var name = ProjectPaths.NameOf(place.Path);
            if (parent.Subfolders.Any(subfolder => string.Equals(subfolder.Name, name, StringComparison.OrdinalIgnoreCase))
                || files.FolderExists(place.Path) || files.Exists(place.Path))
                throw new WorkspaceException(WorkspaceError.Conflict, "A folder or file in that folder already has that name.");
            files.CreateFolder(place.Path);
            places.Folders.Paths[place.FolderId] = place.Path;
            await SaveFilesAsync(files, places.FoldersToSave());
            var changes = new StorageChanges(open.Id);
            changes.FolderPlaces.Add(FolderPlaceModel(open.Id, place.FolderId, place.Path));
            changes.Folders.Add(FolderModel(open, await ReadFolderStateAsync(open, places, parent.Path)));
            return Publish(changes);
        });
    }

    public Task<StorageChanges> UpdateFolderPlaceAsync(FolderPlace place, string expectedETag)
    {
        var open = Get(place.ProjectId);
        return RunLockedAsync(open.Id, async () =>
        {
            var files = open.Files;
            var places = await ReadPlacesAsync(open);
            var oldPath = FolderPathOf(open, places, place.FolderId);
            if (oldPath.Length == 0) throw new WorkspaceException(WorkspaceError.Invalid, "The project folder itself cannot be moved.");
            ETags.Check(FolderPlaceModel(open.Id, place.FolderId, oldPath).ETag, expectedETag);
            var changes = new StorageChanges(open.Id);
            if (place.Path == oldPath)
            {
                changes.FolderPlaces.Add(FolderPlaceModel(open.Id, place.FolderId, oldPath));
                return Publish(changes);
            }
            if (place.Path.Length == 0 || ProjectPaths.IsInside(place.Path, oldPath))
                throw new WorkspaceException(WorkspaceError.Invalid, "A folder cannot move into itself.");
            var target = await ReadFolderStateAsync(open, places, ProjectPaths.ParentOf(place.Path));
            var name = ProjectPaths.NameOf(place.Path);
            if (target.Subfolders.Any(subfolder => subfolder.Id != place.FolderId && string.Equals(subfolder.Name, name, StringComparison.OrdinalIgnoreCase))
                || files.Exists(place.Path) || (files.FolderExists(place.Path) && !string.Equals(place.Path, oldPath, StringComparison.OrdinalIgnoreCase)))
                throw new WorkspaceException(WorkspaceError.Conflict, "The target folder already has a folder or file with that name.");
            files.MoveFolder(oldPath, place.Path);
            foreach (var (id, path) in places.Folders.Paths.ToArray())
                if (path == oldPath || path.StartsWith(oldPath + "/", StringComparison.Ordinal)) places.Folders.Paths[id] = place.Path + path[oldPath.Length..];
            foreach (var (id, path) in places.Documents.Paths.ToArray())
                if (path.StartsWith(oldPath + "/", StringComparison.Ordinal)) places.Documents.Paths[id] = place.Path + path[oldPath.Length..];
            await SaveFilesAsync(files, places.FoldersToSave(), places.DocumentsToSave());
            // The paths of everything inside the folder changed, and the kind of its documents can change too.
            return Publish(await ScanAsync(open));
        });
    }

    public Task<StorageChanges> DeleteFolderPlaceAsync(FolderPlace place, string expectedETag)
    {
        var open = Get(place.ProjectId);
        return RunLockedAsync(open.Id, async () =>
        {
            var files = open.Files;
            var places = await ReadPlacesAsync(open);
            var path = FolderPathOf(open, places, place.FolderId);
            if (path.Length == 0) throw new WorkspaceException(WorkspaceError.Invalid, "The project folder itself cannot be deleted.");
            ETags.Check(FolderPlaceModel(open.Id, place.FolderId, path).ETag, expectedETag);
            files.RemoveEmptyFolder(path);
            places.Folders.Paths.Remove(place.FolderId);
            var changes = new StorageChanges(open.Id);
            changes.RemovedFolderPlaceIds.Add(place.FolderId);
            foreach (var (id, documentPath) in places.Documents.Paths.ToArray())
            {
                if (!documentPath.StartsWith(path + "/", StringComparison.Ordinal)) continue;
                places.Documents.Paths.Remove(id);
                changes.RemovedDocumentPlaceIds.Add(id);
                changes.RemovedDocumentIds.Add(id);
            }
            await SaveFilesAsync(files, places.FoldersToSave(), places.DocumentsToSave());
            changes.Folders.Add(FolderModel(open, await ReadFolderStateAsync(open, places, ProjectPaths.ParentOf(path))));
            return Publish(changes);
        });
    }

    public Task<StorageChanges> AddFolderAsync(Folder folder, FolderPlace place)
    {
        var open = Get(place.ProjectId);
        return RunLockedAsync(open.Id, async () =>
        {
            var places = await ReadPlacesAsync(open);
            var path = FolderPathOf(open, places, folder.Id);
            if (path.Length == 0 || open.Files.Exists(SettingsFiles.FolderFilePath(path), metadata: true))
                throw new WorkspaceException(WorkspaceError.Conflict, "That folder already exists.");
            var file = new FolderFile
            {
                Id = folder.Id, PinnedView = folder.PinnedView, Views = folder.Views.Count == 0 ? null : new(folder.Views, StringComparer.Ordinal),
            };
            await SaveFilesAsync(open.Files, (SettingsFiles.FolderFilePath(path), file, null));
            var changes = new StorageChanges(open.Id);
            changes.Folders.Add(FolderModel(open, await ReadFolderStateAsync(open, places, path)));
            return Publish(changes);
        });
    }

    public Task<StorageChanges> UpdateFolderAsync(Folder folder, FolderPlace place, string expectedETag)
    {
        var open = Get(place.ProjectId);
        return RunLockedAsync(open.Id, async () =>
        {
            var places = await ReadPlacesAsync(open);
            var state = await ReadFolderStateAsync(open, places, FolderPathOf(open, places, folder.Id));
            ETags.Check(FolderModel(open, state).ETag, expectedETag);
            state.File.PinnedView = folder.PinnedView;
            state.File.Views = folder.Views.Count == 0 ? null : new(folder.Views, StringComparer.Ordinal);
            state.File.ItemOrder = OrderChildren(folder.ChildIds, state.Subfolders, state.Documents, isRoot: state.Path.Length == 0);
            await SaveFilesAsync(open.Files, state.ToSave());
            var changes = new StorageChanges(open.Id);
            changes.Folders.Add(FolderModel(open, state));
            return Publish(changes);
        });
    }

    public Task<StorageChanges> DeleteFolderAsync(Folder folder, FolderPlace place, string expectedETag)
    {
        var open = Get(place.ProjectId);
        return RunLockedAsync(open.Id, async () =>
        {
            var places = await ReadPlacesAsync(open);
            var state = await ReadFolderStateAsync(open, places, FolderPathOf(open, places, folder.Id));
            if (state.Path.Length == 0) throw new WorkspaceException(WorkspaceError.Invalid, "The project folder itself cannot be deleted.");
            ETags.Check(FolderModel(open, state).ETag, expectedETag);
            if (state.Subfolders.Count > 0 || state.Documents.Count > 0)
                throw new WorkspaceException(WorkspaceError.Conflict, "Only empty folders can be deleted. Move their files and subfolders first.");
            open.Files.Delete(SettingsFiles.FolderFilePath(state.Path), metadata: true);
            var changes = new StorageChanges(open.Id);
            changes.RemovedFolderIds.Add(folder.Id);
            return Publish(changes);
        });
    }

    public Task<StorageChanges> AddDocumentPlaceAsync(DocumentPlace place)
    {
        var open = Get(place.ProjectId);
        return RunLockedAsync(open.Id, async () =>
        {
            var files = open.Files;
            var places = await ReadPlacesAsync(open);
            if (places.Documents.Paths.ContainsKey(place.DocumentId)) throw new WorkspaceException(WorkspaceError.Conflict, "That document already has a place.");
            var folderPath = ProjectPaths.ParentOf(place.Path);
            if (!(folderPath.Length > 0 && place.Path == DocumentRules.FolderDocumentPath(folderPath))) CheckFileName(ProjectPaths.NameOf(place.Path));
            await ReadFolderStateAsync(open, places, folderPath);
            if (IsTaken(open, places, place.Path, exceptId: null))
                throw new WorkspaceException(WorkspaceError.Conflict, "A document in that folder already has that name.");
            await files.WriteAsync(place.Path, Encode("", ""), overwrite: false);
            places.Documents.Paths[place.DocumentId] = place.Path;
            await SaveFilesAsync(files, places.DocumentsToSave());
            var changes = new StorageChanges(open.Id);
            changes.DocumentPlaces.Add(DocumentPlaceModel(open.Id, place.DocumentId, place.Path));
            changes.Folders.Add(FolderModel(open, await ReadFolderStateAsync(open, places, folderPath)));
            return Publish(changes);
        });
    }

    public Task<StorageChanges> UpdateDocumentPlaceAsync(DocumentPlace place, string expectedETag)
    {
        var open = Get(place.ProjectId);
        return RunLockedAsync(open.Id, async () =>
        {
            var files = open.Files;
            var places = await ReadPlacesAsync(open);
            var oldPath = places.Documents.Paths.GetValueOrDefault(place.DocumentId) ?? throw DocumentGone();
            ETags.Check(DocumentPlaceModel(open.Id, place.DocumentId, oldPath).ETag, expectedETag);
            var changes = new StorageChanges(open.Id);
            if (place.Path == oldPath)
            {
                changes.DocumentPlaces.Add(DocumentPlaceModel(open.Id, place.DocumentId, oldPath));
                return Publish(changes);
            }
            if (DocumentRules.IsFolderDocument(oldPath)) throw new WorkspaceException(WorkspaceError.Invalid, "A folder's own document stays with its folder and keeps its name.");
            CheckFileName(ProjectPaths.NameOf(place.Path));
            var source = await ReadFolderStateAsync(open, places, ProjectPaths.ParentOf(oldPath));
            var target = ProjectPaths.ParentOf(place.Path) == source.Path ? source : await ReadFolderStateAsync(open, places, ProjectPaths.ParentOf(place.Path));
            if (IsTaken(open, places, place.Path, exceptId: place.DocumentId))
                throw new WorkspaceException(WorkspaceError.Conflict, "A document in that folder already has that name.");
            var entry = source.File.Documents.GetValueOrDefault(place.DocumentId) ?? NewEntry(oldPath, 0);
            files.Move(oldPath, place.Path);
            places.Documents.Paths[place.DocumentId] = place.Path;
            source.File.Documents.Remove(place.DocumentId);
            target.File.Documents[place.DocumentId] = entry;
            var saved = new List<(string Path, object Content, byte[]? Before)> { places.DocumentsToSave(), source.ToSave() };
            if (!ReferenceEquals(target, source)) saved.Add(target.ToSave());
            await SaveFilesAsync(files, [.. saved]);
            changes.DocumentPlaces.Add(DocumentPlaceModel(open.Id, place.DocumentId, place.Path));
            var targetState = await ReadFolderStateAsync(open, places, target.Path);
            changes.Documents.Add(DocumentModel(open.Id, targetState.Id, await ReadDocumentFileAsync(files, place.DocumentId, place.Path, entry)));
            changes.Folders.Add(FolderModel(open, targetState));
            if (!ReferenceEquals(target, source)) changes.Folders.Add(FolderModel(open, await ReadFolderStateAsync(open, places, source.Path)));
            return Publish(changes);
        });
    }

    public Task<StorageChanges> DeleteDocumentPlaceAsync(DocumentPlace place, string expectedETag)
    {
        var open = Get(place.ProjectId);
        return RunLockedAsync(open.Id, async () =>
        {
            var places = await ReadPlacesAsync(open);
            var path = places.Documents.Paths.GetValueOrDefault(place.DocumentId) ?? throw DocumentGone();
            ETags.Check(DocumentPlaceModel(open.Id, place.DocumentId, path).ETag, expectedETag);
            if (open.Files.Exists(path)) open.Files.Delete(path);
            places.Documents.Paths.Remove(place.DocumentId);
            await SaveFilesAsync(open.Files, places.DocumentsToSave());
            var changes = new StorageChanges(open.Id);
            changes.RemovedDocumentPlaceIds.Add(place.DocumentId);
            changes.Folders.Add(FolderModel(open, await ReadFolderStateAsync(open, places, ProjectPaths.ParentOf(path))));
            return Publish(changes);
        });
    }

    public Task<StorageChanges> AddDocumentAsync(Document document, DocumentPlace place, string text)
    {
        var open = Get(place.ProjectId);
        return RunLockedAsync(open.Id, async () =>
        {
            var files = open.Files;
            var places = await ReadPlacesAsync(open);
            var path = places.Documents.Paths.GetValueOrDefault(document.Id) ?? throw DocumentGone();
            var state = await ReadFolderStateAsync(open, places, ProjectPaths.ParentOf(path));
            if (state.File.Documents.ContainsKey(document.Id)) throw new WorkspaceException(WorkspaceError.Conflict, "That document already exists.");
            var entry = new DocumentEntry
            {
                Title = document.Title, Synopsis = document.Synopsis, Notes = document.Notes, Status = document.Status, WordGoal = document.WordGoal,
            };
            var bytes = Encode("", text);
            await files.WriteAsync(path, bytes);
            state.File.Documents[document.Id] = entry;
            await SaveFilesAsync(files, state.ToSave());
            var changes = new StorageChanges(open.Id);
            changes.Documents.Add(DocumentModel(open.Id, state.Id, FromBytes(files, document.Id, path, entry, bytes)));
            return Publish(changes);
        });
    }

    public Task<StorageChanges> UpdateDocumentAsync(Document document, DocumentPlace place, string expectedETag)
    {
        var open = Get(place.ProjectId);
        return RunLockedAsync(open.Id, async () =>
        {
            var places = await ReadPlacesAsync(open);
            var (state, current) = await ReadDocumentAsync(open, places, document.Id);
            ETags.Check(DocumentModel(open.Id, state.Id, current).ETag, expectedETag);
            var entry = current.Entry;
            entry.Title = document.Title;
            entry.Synopsis = document.Synopsis;
            entry.Notes = document.Notes;
            entry.Status = document.Status;
            entry.WordGoal = document.WordGoal;
            state.File.Documents[document.Id] = entry;
            await SaveFilesAsync(open.Files, state.ToSave());
            var changes = new StorageChanges(open.Id);
            changes.Documents.Add(DocumentModel(open.Id, state.Id, current with { Entry = entry }));
            return Publish(changes);
        });
    }

    public Task<StorageChanges> DeleteDocumentAsync(Document document, DocumentPlace place, string expectedETag)
    {
        var open = Get(place.ProjectId);
        return RunLockedAsync(open.Id, async () =>
        {
            var places = await ReadPlacesAsync(open);
            var (state, current) = await ReadDocumentAsync(open, places, document.Id);
            ETags.Check(DocumentModel(open.Id, state.Id, current).ETag, expectedETag);
            state.File.Documents.Remove(document.Id);
            await SaveFilesAsync(open.Files, state.ToSave());
            var changes = new StorageChanges(open.Id);
            changes.RemovedDocumentIds.Add(document.Id);
            return Publish(changes);
        });
    }

    public async Task<string> ReadDocumentTextAsync(DocumentPlace place)
    {
        var open = Get(place.ProjectId);
        try { return Split(Decode(await open.Files.ReadAsync(place.Path))).Body; }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { throw DocumentGone(); }
    }

    public Task<StorageChanges> WriteDocumentTextAsync(DocumentPlace place, string text, string expectedETag)
    {
        var open = Get(place.ProjectId);
        return RunLockedAsync(open.Id, async () =>
        {
            var files = open.Files;
            var places = await ReadPlacesAsync(open);
            var (state, current) = await ReadDocumentAsync(open, places, place.DocumentId);
            ETags.Check(DocumentModel(open.Id, state.Id, current).ETag, expectedETag);
            var old = await files.ReadAsync(current.Path);
            var bytes = Encode(Split(Decode(old)).Prefix, text);
            if (!bytes.AsSpan().SequenceEqual(old)) await files.WriteAsync(current.Path, bytes);
            var changes = new StorageChanges(open.Id);
            changes.Documents.Add(DocumentModel(open.Id, state.Id, FromBytes(files, place.DocumentId, current.Path, current.Entry, bytes)));
            return Publish(changes);
        });
    }

    public Task<StorageChanges> AddLinkAsync(Link link)
    {
        var open = Get(link.ProjectId);
        return RunLockedAsync(open.Id, async () =>
        {
            var (file, bytes) = await SettingsFiles.ReadLinksFileAsync(open.Files);
            if (file.Links.Any(entry => link.Joins(entry.FirstDocumentId, entry.SecondDocumentId)))
                throw new WorkspaceException(WorkspaceError.Conflict, "These documents are already linked.");
            var added = new LinkEntry { Id = link.Id, FirstDocumentId = link.FirstDocumentId, SecondDocumentId = link.SecondDocumentId, Note = link.Note };
            file.Links.Add(added);
            await SaveFilesAsync(open.Files, (SettingsFiles.LinksFilePath, file, bytes));
            var changes = new StorageChanges(open.Id);
            changes.Links.Add(LinkModel(open.Id, added));
            return Publish(changes);
        });
    }

    public Task<StorageChanges> UpdateLinkAsync(Link link, string expectedETag)
    {
        var open = Get(link.ProjectId);
        return RunLockedAsync(open.Id, async () =>
        {
            var (file, bytes) = await SettingsFiles.ReadLinksFileAsync(open.Files);
            var entry = file.Links.FirstOrDefault(entry => entry.Id == link.Id) ?? throw LinkGone();
            ETags.Check(LinkModel(open.Id, entry).ETag, expectedETag);
            entry.Note = link.Note;
            await SaveFilesAsync(open.Files, (SettingsFiles.LinksFilePath, file, bytes));
            var changes = new StorageChanges(open.Id);
            changes.Links.Add(LinkModel(open.Id, entry));
            return Publish(changes);
        });
    }

    public Task<StorageChanges> DeleteLinkAsync(Link link, string expectedETag)
    {
        var open = Get(link.ProjectId);
        return RunLockedAsync(open.Id, async () =>
        {
            var (file, bytes) = await SettingsFiles.ReadLinksFileAsync(open.Files);
            var entry = file.Links.FirstOrDefault(entry => entry.Id == link.Id) ?? throw LinkGone();
            ETags.Check(LinkModel(open.Id, entry).ETag, expectedETag);
            file.Links.Remove(entry);
            await SaveFilesAsync(open.Files, (SettingsFiles.LinksFilePath, file, bytes));
            var changes = new StorageChanges(open.Id);
            changes.RemovedLinkIds.Add(entry.Id);
            return Publish(changes);
        });
    }

    private static async Task<PlacesFiles> ReadPlacesAsync(OpenProject open)
    {
        var (folders, folderBytes) = await SettingsFiles.ReadPlacesFileAsync(open.Files, SettingsFiles.FolderPlacesFilePath);
        var (documents, documentBytes) = await SettingsFiles.ReadPlacesFileAsync(open.Files, SettingsFiles.DocumentPlacesFilePath);
        return new PlacesFiles(folders, folderBytes, documents, documentBytes);
    }

    /// <summary>The folder's path. The project's top folder has the empty path.</summary>
    private static string FolderPathOf(OpenProject open, PlacesFiles places, string folderId) =>
        folderId == open.Id ? "" : places.Folders.Paths.GetValueOrDefault(folderId) ?? throw FolderGone();

    /// <summary>Reads a folder's settings file and its children now. A child is a subfolder or document that has a place
    /// and exists on the disk.</summary>
    private static async Task<FolderState> ReadFolderStateAsync(OpenProject open, PlacesFiles places, string path)
    {
        var files = open.Files;
        if (path.Length > 0 && !files.FolderExists(path)) throw FolderGone();
        var (file, bytes) = await SettingsFiles.ReadFolderFileAsync(files, path);
        if (file is null) throw FolderGone();
        string? parentId = null;
        if (path.Length == 0) file.Id = open.Id;
        else
        {
            file.Id = places.Folders.Paths.FirstOrDefault(pair => pair.Value == path).Key ?? throw FolderGone();
            var parentPath = ProjectPaths.ParentOf(path);
            parentId = parentPath.Length == 0 ? open.Id : places.Folders.Paths.FirstOrDefault(pair => pair.Value == parentPath).Key;
        }
        var subfolders = places.Folders.Paths
            .Where(pair => ProjectPaths.ParentOf(pair.Value) == path && files.FolderExists(pair.Value))
            .Select(pair => (pair.Key, ProjectPaths.NameOf(pair.Value))).ToList();
        var own = path.Length == 0 ? null : DocumentRules.FolderDocumentPath(path);
        var inFolder = places.Documents.Paths.Where(pair => ProjectPaths.ParentOf(pair.Value) == path && files.Exists(pair.Value)).ToList();
        var documents = inFolder.Where(pair => pair.Value != own).Select(pair => (pair.Key, ProjectPaths.NameOf(pair.Value))).ToList();
        var ownId = inFolder.FirstOrDefault(pair => pair.Value == own).Key;
        file.ItemOrder = OrderChildren(file.ItemOrder, subfolders, documents, isRoot: path.Length == 0);
        return new FolderState(path, file, bytes, parentId, ownId, subfolders, documents);
    }

    private static Folder FolderModel(OpenProject open, FolderState state) =>
        FolderModel(open, state.Path, state.File, state.ParentId, state.OwnDocumentId);

    /// <summary>The document and the state of the folder it is in. A document without details gets the default details.</summary>
    private static async Task<(FolderState State, DocumentFile Document)> ReadDocumentAsync(OpenProject open, PlacesFiles places, string documentId)
    {
        var path = places.Documents.Paths.GetValueOrDefault(documentId) ?? throw DocumentGone();
        var state = await ReadFolderStateAsync(open, places, ProjectPaths.ParentOf(path));
        var entry = state.File.Documents.GetValueOrDefault(documentId) ?? NewEntry(path, 0);
        try { return (state, await ReadDocumentFileAsync(open.Files, documentId, path, entry)); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { throw DocumentGone(); }
    }

    /// <summary>True when another document has that path, or a file is there, without regard to case.</summary>
    private static bool IsTaken(OpenProject open, PlacesFiles places, string path, string? exceptId)
    {
        var mine = exceptId is null ? null : places.Documents.Paths.GetValueOrDefault(exceptId);
        if (places.Documents.Paths.Any(pair => pair.Key != exceptId && string.Equals(pair.Value, path, StringComparison.OrdinalIgnoreCase))) return true;
        return open.Files.Exists(path) && !string.Equals(mine, path, StringComparison.OrdinalIgnoreCase);
    }

    private static void CheckFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith('.') || name.Contains('/') || name.Contains('\\')
            || !DocumentRules.IsDocument(name) || !Files.FileManager.IsSafePath(name))
            throw new WorkspaceException(WorkspaceError.Invalid, "Use a .md, .markdown or .txt file name.");
    }

    private static WorkspaceException FolderGone() => new(WorkspaceError.NotFound, "The folder no longer exists.");

    private static WorkspaceException DocumentGone() =>
        new(WorkspaceError.NotFound, "This document was removed or moved outside the workspace.");

    private static WorkspaceException LinkGone() => new(WorkspaceError.NotFound, "The link no longer exists.");
}
