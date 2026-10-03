using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Odysseum.Abstractions.Exceptions;
using Odysseum.Server.Models;
using Odysseum.Server.Repositories.Disk.Formats;
using Odysseum.Server.Repositories.Files;
using Odysseum.Server.Services.Documents;
using Odysseum.Server.Services.Projects;
using static Odysseum.Server.Services.Documents.MarkdownDocumentCodec;

namespace Odysseum.Server.Repositories.Disk;

/// <summary>Projects as folders in the workspace: Markdown files, and settings files in each folder's
/// <c>.odysseum</c> folder. It keeps no copy of the data. It only keeps, for each open project, its ID, its folder name
/// and a lock file that stops a second Odysseum from using the same project.</summary>
public sealed partial class DiskStorageContext : IStorageContext, IAsyncDisposable
{
    private static readonly StringComparer NameComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly AsyncLocal<ImmutableHashSet<string>?> HeldLocks = new();

    private readonly FileManager _workspace;
    private readonly OwnWrites? _ownWrites;
    private readonly IProjectWatcher _watcher;
    private readonly ILogger<DiskStorageContext>? _logger;
    private readonly ConcurrentDictionary<string, OpenProject> _projects = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _workspaceGate = new(1, 1);

    public DiskStorageContext(string workspaceRoot, IProjectWatcher watcher, OwnWrites? ownWrites = null, ILogger<DiskStorageContext>? logger = null)
    {
        _workspace = new FileManager(workspaceRoot, ownWrites);
        _ownWrites = ownWrites;
        _watcher = watcher;
        _logger = logger;
        watcher.Changed += OnProjectChangedOutside;
    }

    public event Action<StorageChanges>? Changed;

    public string Root => _workspace.Root;

    private sealed class OpenProject(string id, string name, FileManager files, FileStream instanceLock)
    {
        public string Id { get; } = id;
        public string Name { get; } = name;
        public FileManager Files { get; } = files;
        public FileStream InstanceLock { get; } = instanceLock;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public string? Warning { get; set; }
        public DateTimeOffset LastModified { get; set; }
    }

    /// <summary>A document file as read from the disk, with its details from the folder's settings file.</summary>
    private sealed record DocumentFile(string Id, string Path, DocumentEntry Entry, string FileHash, int WordCount, DateTimeOffset Modified);

    public Task LoadAllProjectsAsync() => LoadNewProjectsAsync();

    public async Task LoadNewProjectsAsync()
    {
        await _workspaceGate.WaitAsync();
        try
        {
            Directory.CreateDirectory(_workspace.Root);
            var names = _workspace.EnumerateFolders(recursive: false).ToList();
            foreach (var gone in _projects.Values.Where(project => !names.Contains(project.Name, NameComparer)).ToArray())
                await CloseAsync(gone, removed: true);
            foreach (var name in names.Where(name => !_projects.Values.Any(project => NameComparer.Equals(project.Name, name))))
            {
                try { await OpenAsync(name); }
                catch (WorkspaceException ex) { _logger?.LogWarning("Project {Project} could not be opened: {Message}", name, ex.Message); }
            }
        }
        finally { _workspaceGate.Release(); }
    }

    public async Task ReloadProjectAsync(string projectId)
    {
        var project = Get(projectId);
        if (!_workspace.FolderExists(project.Name))
        {
            await CloseAsync(project, removed: true);
            return;
        }
        await RunLockedAsync(project.Id, async () => Publish(await ScanAsync(project)));
    }

    public async Task<T> RunLockedAsync<T>(string projectId, Func<Task<T>> work)
    {
        var held = HeldLocks.Value ?? [];
        if (held.Contains(projectId)) return await work();
        var project = Get(projectId);
        await project.Gate.WaitAsync();
        HeldLocks.Value = held.Add(projectId);
        try { return await work(); }
        finally
        {
            HeldLocks.Value = held;
            project.Gate.Release();
        }
    }

    public Task RunLockedAsync(string projectId, Func<Task> work) => RunLockedAsync(projectId, async () =>
    {
        await work();
        return true;
    });

    public async ValueTask DisposeAsync()
    {
        _watcher.Changed -= OnProjectChangedOutside;
        foreach (var project in _projects.Values.ToArray()) await CloseAsync(project, removed: false);
    }

    /// <summary>Opens the project folder, repairs an interrupted save, and reads the project.</summary>
    private async Task<StorageChanges> OpenAsync(string name)
    {
        ProjectNames.Validate(name);
        var files = new FileManager(Path.Combine(_workspace.Root, name), _ownWrites);
        var instanceLock = files.AcquireInstanceLock();
        OpenProject? project = null;
        try
        {
            await new ManifestTransaction(files).RecoverAsync();
            var (file, _) = await SettingsFiles.ReadProjectFileAsync(files);
            // A copied project folder has the ID of the original; the copy gets a new ID.
            var id = file is not null && !_projects.ContainsKey(file.Id) ? file.Id : NewId();
            project = new OpenProject(id, name, files, instanceLock);
            _projects[id] = project;
            _watcher.Watch(name);
            return await RunLockedAsync(id, async () => Publish(await ScanAsync(project)));
        }
        catch
        {
            if (project is not null)
            {
                _projects.TryRemove(project.Id, out _);
                _watcher.Unwatch(name);
            }
            instanceLock.Dispose();
            throw;
        }
    }

    private Task CloseAsync(OpenProject project, bool removed)
    {
        _projects.TryRemove(project.Id, out _);
        _watcher.Unwatch(project.Name);
        project.InstanceLock.Dispose();
        if (removed)
        {
            var changes = new StorageChanges(project.Id) { ReplacesProject = true };
            changes.RemovedProjectIds.Add(project.Id);
            Publish(changes);
        }
        return Task.CompletedTask;
    }

    private void OnProjectChangedOutside(string name)
    {
        if (_projects.Values.FirstOrDefault(project => NameComparer.Equals(project.Name, name)) is { } project)
            _ = ReloadQuietlyAsync(project.Id);
    }

    private async Task ReloadQuietlyAsync(string projectId)
    {
        try { await ReloadProjectAsync(projectId); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or WorkspaceException)
        { _logger?.LogWarning("Reading project {Project} again was deferred: {Message}", projectId, ex.Message); }
    }

    private OpenProject Get(string projectId) => _projects.GetValueOrDefault(projectId)
        ?? throw new WorkspaceException(WorkspaceError.NotFound, "That project no longer exists in the workspace.");

    private StorageChanges Publish(StorageChanges changes)
    {
        Changed?.Invoke(changes);
        return changes;
    }

    /// <summary>Reads every file of the project. It also repairs what other programs changed, and writes the repairs:
    /// <list type="bullet">
    /// <item>A folder that another program moved keeps its ID: its <c>folder.json</c> has the ID, and its old path is gone.
    /// The documents in it keep their IDs too.</item>
    /// <item>A copied folder, and a folder or file that is not in <c>folders.json</c> or <c>documents.json</c>, gets a new ID.</item>
    /// <item>A path whose file or folder is gone is removed, and so are the details of a document that is gone.</item>
    /// <item>A folder without its own hidden document gets one.</item>
    /// <item>Each folder's order lists exactly its children.</item>
    /// </list></summary>
    private async Task<StorageChanges> ScanAsync(OpenProject project)
    {
        var files = project.Files;
        var warnings = new List<string>();

        var (projectFile, projectBytes) = await SettingsFiles.ReadProjectFileAsync(files);
        projectFile ??= new ProjectFile { Settings = new() { Title = project.Name } };
        projectFile.Id = project.Id;
        var (folderPlaces, folderPlacesBytes) = await SettingsFiles.ReadPlacesFileAsync(files, SettingsFiles.FolderPlacesFilePath);
        var (documentPlaces, documentPlacesBytes) = await SettingsFiles.ReadPlacesFileAsync(files, SettingsFiles.DocumentPlacesFilePath);

        // Folders: the ID comes from folders.json, or from the folder's own folder.json when another program moved it.
        var folderPaths = files.EnumerateFolders().ToList();
        var folderPathSet = folderPaths.ToHashSet(StringComparer.Ordinal);
        var read = new Dictionary<string, (FolderFile? File, byte[]? Bytes)>(StringComparer.Ordinal);
        foreach (var path in folderPaths) read[path] = await SettingsFiles.ReadFolderFileAsync(files, path);
        var folderIdByPath = folderPlaces.Paths.Where(pair => folderPathSet.Contains(pair.Value))
            .ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);
        var assigned = new HashSet<string>(folderIdByPath.Values, StringComparer.Ordinal) { project.Id };
        var moved = new List<(string From, string To)>();
        foreach (var path in folderPaths.Where(path => !folderIdByPath.ContainsKey(path)))
        {
            var ownId = read[path].File?.Id;
            if (ownId is not null && !assigned.Contains(ownId) && folderPlaces.Paths.TryGetValue(ownId, out var oldPath) && !folderPathSet.Contains(oldPath))
            {
                folderIdByPath[path] = ownId;
                moved.Add((oldPath, path));
            }
            else folderIdByPath[path] = NewId();
            assigned.Add(folderIdByPath[path]);
        }
        folderPlaces.Paths = folderIdByPath.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);
        var folders = new Dictionary<string, FolderFile>(StringComparer.Ordinal) { [""] = projectFile };
        foreach (var path in folderPaths)
        {
            var file = read[path].File ?? new FolderFile();
            file.Id = folderIdByPath[path];
            folders[path] = file;
        }

        // Documents: the ID comes from documents.json. A document in a moved folder keeps its ID.
        string MovedPath(string path)
        {
            foreach (var (from, to) in moved.OrderByDescending(item => item.From.Length))
                if (path.StartsWith(from + "/", StringComparison.Ordinal)) return to + path[from.Length..];
            return path;
        }
        var documentPaths = files.EnumerateDocuments().Order(StringComparer.Ordinal).ToList();
        var documentPathSet = documentPaths.ToHashSet(StringComparer.Ordinal);
        var documentIdByPath = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, registered) in documentPlaces.Paths)
        {
            var path = documentPathSet.Contains(registered) ? registered : MovedPath(registered);
            if (documentPathSet.Contains(path)) documentIdByPath.TryAdd(path, id);
        }
        var entryOwner = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (path, file) in folders)
            foreach (var id in file.Documents.Keys) entryOwner.TryAdd(id, path);
        var documents = new Dictionary<string, DocumentFile>(StringComparer.Ordinal);
        var unreadable = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in documentPaths)
        {
            if (!folders.TryGetValue(ProjectPaths.ParentOf(path), out var owner)) continue;
            if (!documentIdByPath.TryGetValue(path, out var id)) documentIdByPath[path] = id = NewId();
            // The details belong in the folder the document is in; they move there if a save was interrupted.
            if (!owner.Documents.TryGetValue(id, out var entry))
            {
                entry = entryOwner.TryGetValue(id, out var other) && folders[other].Documents.Remove(id, out var found) ? found
                    : NewEntry(path, projectFile.Settings.DefaultSceneWordGoal);
                owner.Documents[id] = entry;
            }
            try { documents[id] = await ReadDocumentFileAsync(files, id, path, entry); }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { }
            catch (DecoderFallbackException) { unreadable[id] = path; warnings.Add($"{path} is not UTF-8 and was left untouched."); }
            catch (WorkspaceException ex) when (ex.Error == WorkspaceError.TooLarge) { unreadable[id] = path; warnings.Add($"{path}: {ex.Message}"); }
            catch (IOException) { throw new WorkspaceException(WorkspaceError.Unavailable, "A document is still being written. Odysseum will try again soon."); }
        }
        foreach (var (path, file) in folders)
            foreach (var id in file.Documents.Keys.ToArray())
            {
                var here = documents.TryGetValue(id, out var document) ? ProjectPaths.ParentOf(document.Path) : unreadable.TryGetValue(id, out var other) ? ProjectPaths.ParentOf(other) : null;
                if (here != path) file.Documents.Remove(id);
            }
        foreach (var (path, file) in folders.Where(pair => pair.Key.Length > 0))
        {
            var own = DocumentRules.FolderDocumentPath(path);
            if (documentPathSet.Contains(own) || files.Exists(own)) continue;
            var bytes = Encode("", "");
            await files.WriteAsync(own, bytes, overwrite: false);
            var id = NewId();
            var entry = NewEntry(own, 0);
            file.Documents[id] = entry;
            documents[id] = new DocumentFile(id, own, entry, ETags.Hash(bytes), 0, ModifiedOf(files, own));
        }
        documentPlaces.Paths = documents.Values.Select(document => (document.Id, document.Path))
            .Concat(unreadable.Select(pair => (pair.Key, pair.Value)))
            .ToDictionary(pair => pair.Item1, pair => pair.Item2, StringComparer.Ordinal);

        foreach (var (path, file) in folders)
        {
            var subfolders = folders.Where(pair => pair.Key.Length > 0 && ProjectPaths.ParentOf(pair.Key) == path)
                .Select(pair => (pair.Value.Id, ProjectPaths.NameOf(pair.Key)));
            var children = documents.Values.Where(document => ProjectPaths.ParentOf(document.Path) == path && !DocumentRules.IsFolderDocument(document.Path))
                .Select(document => (document.Id, ProjectPaths.NameOf(document.Path)));
            file.ItemOrder = OrderChildren(file.ItemOrder, subfolders, children, isRoot: path.Length == 0);
        }
        var saved = new List<(string Path, object Content, byte[]? Before)>
        {
            (SettingsFiles.ProjectFilePath, projectFile, projectBytes),
            (SettingsFiles.FolderPlacesFilePath, folderPlaces, folderPlacesBytes),
            (SettingsFiles.DocumentPlacesFilePath, documentPlaces, documentPlacesBytes),
        };
        saved.AddRange(folderPaths.Select(path => (SettingsFiles.FolderFilePath(path), (object)folders[path], read[path].Bytes)));
        await SaveFilesAsync(files, [.. saved]);

        var (linksFile, _) = await SettingsFiles.ReadLinksFileAsync(files);
        project.Warning = warnings.Count > 0 ? string.Join(" ", warnings) : null;
        project.LastModified = documents.Values.Select(document => document.Modified).DefaultIfEmpty(ModifiedOf(files, SettingsFiles.ProjectFilePath)).Max();

        var changes = new StorageChanges(project.Id) { ReplacesProject = true };
        changes.Projects.Add(ProjectModel(project, projectFile));
        foreach (var (path, file) in folders)
        {
            var own = path.Length == 0 ? null : documents.Values.FirstOrDefault(document => document.Path == DocumentRules.FolderDocumentPath(path))?.Id;
            var parentId = path.Length == 0 ? null : folders[ProjectPaths.ParentOf(path)].Id;
            changes.Folders.Add(FolderModel(project, path, file, parentId, own));
        }
        foreach (var document in documents.Values)
            changes.Documents.Add(DocumentModel(project.Id, folders[ProjectPaths.ParentOf(document.Path)].Id, document));
        foreach (var link in linksFile.Links.Where(link => documents.ContainsKey(link.FirstDocumentId) && documents.ContainsKey(link.SecondDocumentId)))
            changes.Links.Add(LinkModel(project.Id, link));
        return changes;
    }

    /// <summary>The order of a folder's children: the listed IDs that are still children, then the unlisted subfolders,
    /// then the unlisted documents. In the project's top folder, the default folders come first.</summary>
    private static List<string> OrderChildren(IReadOnlyList<string> listed, IEnumerable<(string Id, string Name)> folders,
        IEnumerable<(string Id, string Name)> documents, bool isRoot)
    {
        var folderList = folders.ToList();
        var documentList = documents.ToList();
        var children = folderList.Select(folder => folder.Id).Concat(documentList.Select(document => document.Id)).ToHashSet(StringComparer.Ordinal);
        var order = listed.Where(children.Contains).Distinct(StringComparer.Ordinal).ToList();
        var seen = order.ToHashSet(StringComparer.Ordinal);
        order.AddRange(folderList.Where(folder => !seen.Contains(folder.Id))
            .OrderBy(folder => isRoot ? DefaultFolders.Rank(folder.Name) : 0).ThenBy(folder => folder.Name, StringComparer.Ordinal).Select(folder => folder.Id));
        order.AddRange(documentList.Where(document => !seen.Contains(document.Id)).OrderBy(document => document.Name, StringComparer.Ordinal).Select(document => document.Id));
        return order;
    }

    private static async Task<DocumentFile> ReadDocumentFileAsync(IFileManager files, string id, string path, DocumentEntry entry)
    {
        var bytes = await files.ReadAsync(path);
        return FromBytes(files, id, path, entry, bytes);
    }

    private static DocumentFile FromBytes(IFileManager files, string id, string path, DocumentEntry entry, byte[] bytes) =>
        new(id, path, entry, ETags.Hash(bytes), CountWords(Split(Decode(bytes)).Body), ModifiedOf(files, path));

    private static DocumentEntry NewEntry(string path, int defaultWordGoal)
    {
        var folderDocument = DocumentRules.IsFolderDocument(path);
        return new DocumentEntry
        {
            Title = folderDocument ? ProjectPaths.NameOf(ProjectPaths.ParentOf(path)) : Path.GetFileNameWithoutExtension(path),
            WordGoal = folderDocument ? 0 : defaultWordGoal,
        };
    }

    private static Project ProjectModel(OpenProject project, ProjectFile file)
    {
        var settings = file.Settings;
        return new Project(project.Id, project.Name, settings.Title, settings.WordGoal, settings.DefaultSceneWordGoal,
            ETags.FromValues(project.Id, settings.Title, settings.WordGoal, settings.DefaultSceneWordGoal), project.LastModified, project.Warning);
    }

    private static Folder FolderModel(OpenProject project, string path, FolderFile file, string? parentFolderId, string? ownDocumentId)
    {
        var views = new Dictionary<string, JsonElement>(file.Views ?? [], StringComparer.Ordinal);
        var viewsText = JsonSerializer.Serialize(views.OrderBy(view => view.Key, StringComparer.Ordinal).ToDictionary());
        var etag = ETags.FromValues(file.Id, path, file.PinnedView, viewsText, string.Join(',', file.ItemOrder));
        return new Folder(file.Id, project.Id, path.Length == 0 ? project.Name : ProjectPaths.NameOf(path), path, parentFolderId,
            file.ItemOrder.ToArray(), ownDocumentId, file.PinnedView, views, etag);
    }

    private static Document DocumentModel(string projectId, string folderId, DocumentFile file)
    {
        var entry = file.Entry;
        var etag = ETags.FromValues(file.Id, file.Path, entry.Title, entry.Synopsis, entry.Notes, entry.Status, entry.WordGoal, file.FileHash);
        return new Document(file.Id, projectId, folderId, ProjectPaths.NameOf(file.Path), file.Path, DocumentRules.KindOf(file.Path),
            DocumentRules.IsFolderDocument(file.Path), entry.Title, entry.Synopsis, entry.Notes, entry.Status, entry.WordGoal,
            file.WordCount, file.Modified, etag);
    }

    private static Link LinkModel(string projectId, LinkEntry entry) => new(entry.Id, projectId, entry.FirstDocumentId,
        entry.SecondDocumentId, entry.Note, ETags.FromValues(entry.Id, entry.FirstDocumentId, entry.SecondDocumentId, entry.Note));

    /// <summary>Writes the settings files that changed, together, so that either all of them change or none.</summary>
    private static async Task SaveFilesAsync(IFileManager files, params (string Path, object Content, byte[]? Before)[] entries)
    {
        var after = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var before = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (path, content, old) in entries)
        {
            var bytes = SettingsFiles.Serialize(content);
            if (old is not null && old.AsSpan().SequenceEqual(bytes)) continue;
            after[path] = bytes;
            if (old is not null) before[path] = old;
        }
        if (after.Count > 0) await new ManifestTransaction(files).CommitAsync(after, before);
    }

    private static DateTimeOffset ModifiedOf(IFileManager files, string path) =>
        new(DateTime.SpecifyKind(files.LastModified(path, path.StartsWith(".odysseum/", StringComparison.Ordinal)), DateTimeKind.Utc));

    private static string NewId() => Guid.NewGuid().ToString();
}
