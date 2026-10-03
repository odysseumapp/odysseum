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

    public Task<StorageChanges> AddFolderAsync(Folder folder)
    {
        var open = Get(folder.ProjectId);
        return RunLockedAsync(open.Id, async () =>
        {
            var files = open.Files;
            var places = await ReadPlacesAsync(open);
            if (folder.Id == open.Id || places.Folders.Paths.ContainsKey(folder.Id)) throw new WorkspaceException(WorkspaceError.Conflict, "That folder already exists.");
            var parentPath = FolderPathOf(open, places, folder.ParentFolderId ?? throw new WorkspaceException(WorkspaceError.Invalid, "A new folder needs a parent folder."));
            var parent = await ReadFolderStateAsync(open, places, parentPath);
            var path = ProjectPaths.Join(parentPath, folder.Name);
            if (parent.Subfolders.Any(subfolder => string.Equals(subfolder.Name, folder.Name, StringComparison.OrdinalIgnoreCase))
                || files.FolderExists(path) || files.Exists(path))
                throw new WorkspaceException(WorkspaceError.Conflict, "A folder or file in that folder already has that name.");
            files.CreateFolder(path);
            var ownId = NewId();
            var ownPath = DocumentRules.FolderDocumentPath(path);
            var ownBytes = Encode("", "");
            var ownEntry = NewEntry(ownPath, 0);
            await files.WriteAsync(ownPath, ownBytes, overwrite: false);
            var file = new FolderFile
            {
                Id = folder.Id, PinnedView = folder.PinnedView, Views = folder.Views.Count == 0 ? null : new(folder.Views, StringComparer.Ordinal),
                Documents = { [ownId] = ownEntry },
            };
            places.Folders.Paths[folder.Id] = path;
            places.Documents.Paths[ownId] = ownPath;
            parent = await ReadFolderStateAsync(open, places, parentPath);
            await SaveFilesAsync(files, places.FoldersToSave(), places.DocumentsToSave(), (SettingsFiles.FolderFilePath(path), file, null), parent.ToSave());
            var changes = new StorageChanges(open.Id);
            changes.Folders.Add(FolderModel(open, await ReadFolderStateAsync(open, places, path)));
            changes.Folders.Add(FolderModel(open, parent));
            changes.Documents.Add(DocumentModel(open.Id, folder.Id, FromBytes(files, ownId, ownPath, ownEntry, ownBytes)));
            return Publish(changes);
        });
    }

    public Task<StorageChanges> UpdateFolderAsync(Folder folder, string expectedETag)
    {
        var open = Get(folder.ProjectId);
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

    public Task<StorageChanges> MoveFolderAsync(Folder folder, string targetParentId, int index, string expectedETag)
    {
        var open = Get(folder.ProjectId);
        return RunLockedAsync(open.Id, async () =>
        {
            var files = open.Files;
            var places = await ReadPlacesAsync(open);
            var state = await ReadFolderStateAsync(open, places, FolderPathOf(open, places, folder.Id));
            if (state.Path.Length == 0) throw new WorkspaceException(WorkspaceError.Invalid, "The project folder itself cannot be moved.");
            ETags.Check(FolderModel(open, state).ETag, expectedETag);
            var oldPath = state.Path;
            var targetPath = FolderPathOf(open, places, targetParentId);
            if (targetParentId == state.ParentId)
            {
                var parent = await ReadFolderStateAsync(open, places, targetPath);
                parent.File.ItemOrder = PutAt(parent.File.ItemOrder, folder.Id, index);
                await SaveFilesAsync(files, parent.ToSave());
                var reordered = new StorageChanges(open.Id);
                reordered.Folders.Add(FolderModel(open, state));
                reordered.Folders.Add(FolderModel(open, parent));
                return Publish(reordered);
            }
            var path = ProjectPaths.Join(targetPath, ProjectPaths.NameOf(oldPath));
            if (ProjectPaths.IsInside(path, oldPath)) throw new WorkspaceException(WorkspaceError.Invalid, "A folder cannot move into itself.");
            var target = await ReadFolderStateAsync(open, places, targetPath);
            var name = ProjectPaths.NameOf(path);
            if (target.Subfolders.Any(subfolder => string.Equals(subfolder.Name, name, StringComparison.OrdinalIgnoreCase)) || files.Exists(path) || files.FolderExists(path))
                throw new WorkspaceException(WorkspaceError.Conflict, "The target folder already has a folder or file with that name.");
            files.MoveFolder(oldPath, path);
            foreach (var (id, folderPath) in places.Folders.Paths.ToArray())
                if (ProjectPaths.IsInside(folderPath, oldPath)) places.Folders.Paths[id] = path + folderPath[oldPath.Length..];
            foreach (var (id, documentPath) in places.Documents.Paths.ToArray())
                if (ProjectPaths.IsInside(documentPath, oldPath)) places.Documents.Paths[id] = path + documentPath[oldPath.Length..];
            target = await ReadFolderStateAsync(open, places, targetPath);
            target.File.ItemOrder = PutAt(target.File.ItemOrder, folder.Id, index);
            await SaveFilesAsync(files, places.FoldersToSave(), places.DocumentsToSave(), target.ToSave());
            // The paths of everything inside the folder changed, and the kind of its documents can change too.
            var changes = await ScanAsync(open);
            changes.MovedIds.Add(folder.Id);
            return Publish(changes);
        });
    }

    public Task<StorageChanges> DeleteFolderAsync(Folder folder, string expectedETag)
    {
        var open = Get(folder.ProjectId);
        return RunLockedAsync(open.Id, async () =>
        {
            var files = open.Files;
            var places = await ReadPlacesAsync(open);
            var state = await ReadFolderStateAsync(open, places, FolderPathOf(open, places, folder.Id));
            if (state.Path.Length == 0) throw new WorkspaceException(WorkspaceError.Invalid, "The project folder itself cannot be deleted.");
            ETags.Check(FolderModel(open, state).ETag, expectedETag);
            if (state.Subfolders.Count > 0 || state.Documents.Count > 0)
                throw new WorkspaceException(WorkspaceError.Conflict, "Only empty folders can be deleted. Move their files and subfolders first.");
            files.RemoveEmptyFolder(state.Path);
            var changes = new StorageChanges(open.Id);
            places.Folders.Paths.Remove(folder.Id);
            changes.RemovedFolderIds.Add(folder.Id);
            foreach (var (id, documentPath) in places.Documents.Paths.ToArray())
            {
                if (!ProjectPaths.IsInside(documentPath, state.Path)) continue;
                places.Documents.Paths.Remove(id);
                changes.RemovedDocumentIds.Add(id);
            }
            var parent = await ReadFolderStateAsync(open, places, ProjectPaths.ParentOf(state.Path));
            await SaveFilesAsync(files, places.FoldersToSave(), places.DocumentsToSave(), parent.ToSave());
            changes.Folders.Add(FolderModel(open, parent));
            await AddRemovedLinksAsync(open, changes);
            return Publish(changes);
        });
    }

    public Task<StorageChanges> AddDocumentAsync(Document document, string text)
    {
        var open = Get(document.ProjectId);
        return RunLockedAsync(open.Id, async () =>
        {
            var files = open.Files;
            var places = await ReadPlacesAsync(open);
            if (places.Documents.Paths.ContainsKey(document.Id)) throw new WorkspaceException(WorkspaceError.Conflict, "That document already exists.");
            CheckFileName(document.Name);
            var folderPath = FolderPathOf(open, places, document.FolderId);
            await ReadFolderStateAsync(open, places, folderPath);
            var path = ProjectPaths.Join(folderPath, document.Name);
            if (IsTaken(open, places, path, exceptId: null))
                throw new WorkspaceException(WorkspaceError.Conflict, "A document in that folder already has that name.");
            var bytes = Encode("", text);
            await files.WriteAsync(path, bytes, overwrite: false);
            places.Documents.Paths[document.Id] = path;
            var state = await ReadFolderStateAsync(open, places, folderPath);
            var entry = new DocumentEntry
            {
                Title = document.Title, Synopsis = document.Synopsis, Notes = document.Notes, Status = document.Status, WordGoal = document.WordGoal,
            };
            state.File.Documents[document.Id] = entry;
            await SaveFilesAsync(files, places.DocumentsToSave(), state.ToSave());
            var changes = new StorageChanges(open.Id);
            changes.Documents.Add(DocumentModel(open.Id, state.Id, FromBytes(files, document.Id, path, entry, bytes)));
            changes.Folders.Add(FolderModel(open, state));
            return Publish(changes);
        });
    }

    public Task<StorageChanges> UpdateDocumentAsync(Document document, string expectedETag)
    {
        var open = Get(document.ProjectId);
        return RunLockedAsync(open.Id, async () =>
        {
            var files = open.Files;
            var places = await ReadPlacesAsync(open);
            var (state, current) = await ReadDocumentAsync(open, places, document.Id);
            ETags.Check(DocumentModel(open.Id, state.Id, current).ETag, expectedETag);
            var path = current.Path;
            if (document.Name != ProjectPaths.NameOf(path))
            {
                if (DocumentRules.IsFolderDocument(path)) throw new WorkspaceException(WorkspaceError.Invalid, "A folder's own document keeps its name.");
                CheckFileName(document.Name);
                path = ProjectPaths.Join(state.Path, document.Name);
                if (IsTaken(open, places, path, exceptId: document.Id))
                    throw new WorkspaceException(WorkspaceError.Conflict, "A document in that folder already has that name.");
                files.Move(current.Path, path);
                places.Documents.Paths[document.Id] = path;
            }
            var entry = current.Entry;
            entry.Title = document.Title;
            entry.Synopsis = document.Synopsis;
            entry.Notes = document.Notes;
            entry.Status = document.Status;
            entry.WordGoal = document.WordGoal;
            state.File.Documents[document.Id] = entry;
            await SaveFilesAsync(files, places.DocumentsToSave(), state.ToSave());
            var changes = new StorageChanges(open.Id);
            changes.Documents.Add(DocumentModel(open.Id, state.Id, current with { Path = path, Entry = entry }));
            return Publish(changes);
        });
    }

    public Task<StorageChanges> MoveDocumentAsync(Document document, string targetFolderId, int index, string expectedETag)
    {
        var open = Get(document.ProjectId);
        return RunLockedAsync(open.Id, async () =>
        {
            var files = open.Files;
            var places = await ReadPlacesAsync(open);
            var (source, current) = await ReadDocumentAsync(open, places, document.Id);
            ETags.Check(DocumentModel(open.Id, source.Id, current).ETag, expectedETag);
            if (DocumentRules.IsFolderDocument(current.Path)) throw new WorkspaceException(WorkspaceError.Invalid, "A folder's own document stays with its folder.");
            var changes = new StorageChanges(open.Id);
            if (targetFolderId == source.Id)
            {
                source.File.ItemOrder = PutAt(source.File.ItemOrder, document.Id, index);
                await SaveFilesAsync(files, source.ToSave());
                changes.Documents.Add(DocumentModel(open.Id, source.Id, current));
                changes.Folders.Add(FolderModel(open, source));
                return Publish(changes);
            }
            var targetPath = FolderPathOf(open, places, targetFolderId);
            if (targetPath.Length > 0 && !files.FolderExists(targetPath)) throw FolderGone();
            var path = ProjectPaths.Join(targetPath, ProjectPaths.NameOf(current.Path));
            if (IsTaken(open, places, path, exceptId: document.Id))
                throw new WorkspaceException(WorkspaceError.Conflict, "A document in that folder already has that name.");
            files.Move(current.Path, path);
            places.Documents.Paths[document.Id] = path;
            source = await ReadFolderStateAsync(open, places, source.Path);
            var target = await ReadFolderStateAsync(open, places, targetPath);
            source.File.Documents.Remove(document.Id);
            target.File.Documents[document.Id] = current.Entry;
            target.File.ItemOrder = PutAt(target.File.ItemOrder, document.Id, index);
            await SaveFilesAsync(files, places.DocumentsToSave(), source.ToSave(), target.ToSave());
            changes.Documents.Add(DocumentModel(open.Id, target.Id, current with { Path = path }));
            changes.Folders.Add(FolderModel(open, source));
            changes.Folders.Add(FolderModel(open, target));
            changes.MovedIds.Add(document.Id);
            return Publish(changes);
        });
    }

    public Task<StorageChanges> DeleteDocumentAsync(Document document, string expectedETag)
    {
        var open = Get(document.ProjectId);
        return RunLockedAsync(open.Id, async () =>
        {
            var places = await ReadPlacesAsync(open);
            var (state, current) = await ReadDocumentAsync(open, places, document.Id);
            ETags.Check(DocumentModel(open.Id, state.Id, current).ETag, expectedETag);
            if (DocumentRules.IsFolderDocument(current.Path)) throw new WorkspaceException(WorkspaceError.Invalid, "A folder's own document goes only with its folder.");
            open.Files.Delete(current.Path);
            places.Documents.Paths.Remove(document.Id);
            state = await ReadFolderStateAsync(open, places, state.Path);
            state.File.Documents.Remove(document.Id);
            await SaveFilesAsync(open.Files, places.DocumentsToSave(), state.ToSave());
            var changes = new StorageChanges(open.Id);
            changes.RemovedDocumentIds.Add(document.Id);
            changes.Folders.Add(FolderModel(open, state));
            await AddRemovedLinksAsync(open, changes);
            return Publish(changes);
        });
    }

    public async Task<string> ReadDocumentTextAsync(Document document)
    {
        var open = Get(document.ProjectId);
        try { return Split(Decode(await open.Files.ReadAsync(document.Path))).Body; }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { throw DocumentGone(); }
    }

    public Task<StorageChanges> WriteDocumentTextAsync(Document document, string text, string expectedETag)
    {
        var open = Get(document.ProjectId);
        return RunLockedAsync(open.Id, async () =>
        {
            var files = open.Files;
            var places = await ReadPlacesAsync(open);
            var (state, current) = await ReadDocumentAsync(open, places, document.Id);
            ETags.Check(DocumentModel(open.Id, state.Id, current).ETag, expectedETag);
            var old = await files.ReadAsync(current.Path);
            var bytes = Encode(Split(Decode(old)).Prefix, text);
            if (!bytes.AsSpan().SequenceEqual(old)) await files.WriteAsync(current.Path, bytes);
            var changes = new StorageChanges(open.Id);
            changes.Documents.Add(DocumentModel(open.Id, state.Id, FromBytes(files, document.Id, current.Path, current.Entry, bytes)));
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

    /// <summary>The order with the item at <paramref name="index"/>. The index is clamped.</summary>
    private static List<string> PutAt(IEnumerable<string> order, string id, int index)
    {
        var result = order.Where(child => child != id).ToList();
        result.Insert(Math.Clamp(index, 0, result.Count), id);
        return result;
    }

    /// <summary>Adds the links to the removed documents. <c>links.json</c> keeps them, so that they come back when a
    /// version brings the document back.</summary>
    private static async Task AddRemovedLinksAsync(OpenProject open, StorageChanges changes)
    {
        var (file, _) = await SettingsFiles.ReadLinksFileAsync(open.Files);
        var removed = changes.RemovedDocumentIds.ToHashSet(StringComparer.Ordinal);
        changes.RemovedLinkIds.AddRange(file.Links
            .Where(link => removed.Contains(link.FirstDocumentId) || removed.Contains(link.SecondDocumentId)).Select(link => link.Id));
    }

    /// <summary>Reads a folder's settings file and its children now. A child is a subfolder or document that is in
    /// <c>folders.json</c> or <c>documents.json</c> and exists on the disk.</summary>
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
