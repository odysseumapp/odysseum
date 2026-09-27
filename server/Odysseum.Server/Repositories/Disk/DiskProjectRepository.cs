using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Odysseum.Abstractions.Exceptions;
using Odysseum.Abstractions.Folders;
using Odysseum.Abstractions.Projects;
using Odysseum.Server.Models;
using Odysseum.Server.Repositories.Files;
using Odysseum.Server.Repositories.Manifests;
using Odysseum.Server.Services.Projects;
using static Odysseum.Server.Repositories.ContentRevision;
using static Odysseum.Server.Services.Documents.DocumentRules;
using static Odysseum.Server.Services.Documents.MarkdownDocumentCodec;
using ProjectSettings = Odysseum.Server.Settings.ProjectSettings;

namespace Odysseum.Server.Repositories.Disk;

/// <summary>Projects as folders in the workspace: Markdown files, <c>project.json</c> and one <c>folder.json</c> per
/// folder. Every load scans the files. Only the <c>main</c> branch exists.</summary>
public sealed class DiskProjectRepository(string workspaceRoot, OwnWrites? ownWrites = null) : IProjectRepository
{
    private readonly FileManager _workspace = new(workspaceRoot);
    private readonly ConcurrentDictionary<string, DiskProject> _projects =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    /// <summary>Project ID to project name, from the last listing and the loads since. <c>project.json</c> is the true
    /// record; <see cref="FindNameAsync"/> checks each entry against it and lists again when it is wrong or missing.</summary>
    private readonly ConcurrentDictionary<string, string> _ids = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _createGate = new(1, 1);

    public string Root => _workspace.Root;

    private sealed class DiskProject(string name, FileManager files)
    {
        public string Name { get; } = name;
        public FileManager Files { get; } = files;
        public ManifestFiles Manifests { get; } = new(files);
        /// <summary>Document ID to path, from the last load.</summary>
        public Dictionary<string, string> Paths { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class Lease(FileStream instanceLock) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            instanceLock.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed record ScannedFile(string Id, string Path, string Revision, DateTimeOffset Modified, int WordCount);

    public IEnumerable<string> Names()
    {
        _workspace.CreateFolder("");
        return _workspace.EnumerateFolders(recursive: false);
    }

    public async Task<IReadOnlyList<ProjectInfo>> ListAsync()
    {
        var projects = new List<ProjectInfo>();
        foreach (var name in Names()) projects.Add(await DescribeAsync(name));
        return projects.OrderBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.Name, StringComparer.Ordinal).ToArray();
    }

    public async Task<string?> FindNameAsync(string id)
    {
        if (_ids.TryGetValue(id, out var name) && await IdOfAsync(name) is { } stored && string.Equals(stored, id, StringComparison.OrdinalIgnoreCase))
            return name;
        _ids.Clear();
        await ListAsync();
        return _ids.GetValueOrDefault(id);
    }

    public Task<bool> ExistsAsync(ProjectBranch branch) =>
        Task.FromResult(branch.IsMain && _workspace.FolderExists(ProjectNames.Validate(branch.Project)));

    public async Task<ProjectInfo> CreateAsync(string title)
    {
        var stem = FileName(ValidateTitle(title));
        string name;
        await _createGate.WaitAsync();
        try
        {
            _workspace.CreateFolder("");
            name = stem;
            var suffix = 2;
            while (_workspace.FolderExists(name) || _workspace.Exists(name)) name = $"{stem}-{suffix++}";
            _workspace.CreateFolder(name);
        }
        finally { _createGate.Release(); }
        return await DescribeAsync(name);
    }

    public async Task<IAsyncDisposable> OpenAsync(ProjectBranch branch)
    {
        var project = Get(branch);
        var instanceLock = project.Files.AcquireInstanceLock();
        try { await new ManifestTransaction(project.Files).RecoverAsync(); }
        catch
        {
            instanceLock.Dispose();
            throw;
        }
        return new Lease(instanceLock);
    }

    public Task<ProjectData> LoadAsync(ProjectBranch branch) => LoadAsync(Get(branch));

    public async Task<string> LoadBodyAsync(ProjectBranch branch, string documentId)
    {
        var project = Get(branch);
        var path = project.Paths.GetValueOrDefault(documentId) ?? throw MissingDocument();
        byte[] bytes;
        try { bytes = await project.Files.ReadAsync(path); }
        catch (FileNotFoundException) { throw MissingDocument(); }
        catch (DirectoryNotFoundException) { throw MissingDocument(); }
        return Split(Decode(bytes)).Body;
    }

    public async Task<ProjectData> SaveAsync(ProjectBranch branch, Changes changes, string revision)
    {
        var project = Get(branch);
        var files = project.Files;
        var (persisted, persistedRevision) = await project.Manifests.ReadAsync();
        if (persistedRevision != revision) throw new WorkspaceException(WorkspaceError.Conflict, "The project changed. Refresh before saving again.");
        persisted ??= NewManifest(project.Name);

        // Folders change on disk first; the manifests are read again afterwards, because they move with their folders.
        var folderPaths = FolderPaths(persisted);
        var foldersChanged = false;
        foreach (var folder in changes.Folders.Where(f => !f.IsRoot))
        {
            if (folderPaths.TryGetValue(folder.Id, out var oldPath))
            {
                if (oldPath == folder.Path) continue;
                files.MoveFolder(oldPath, folder.Path);
                foreach (var (id, path) in folderPaths.ToArray())
                    if (path == oldPath || path.StartsWith(oldPath + "/", StringComparison.Ordinal))
                        folderPaths[id] = folder.Path + path[oldPath.Length..];
            }
            else
            {
                files.CreateFolder(folder.Path);
                folderPaths[folder.Id] = folder.Path;
            }
            foldersChanged = true;
        }
        foreach (var id in changes.RemovedFolders)
        {
            if (!folderPaths.Remove(id, out var path)) continue;
            files.RemoveEmptyFolder(path);
            foldersChanged = true;
        }
        if (foldersChanged) (persisted, persistedRevision) = await project.Manifests.ReadAsync();
        var candidate = (persisted ?? NewManifest(project.Name)).Clone();

        if (changes.Settings is { } settings)
        {
            candidate.Settings = ProjectSettings.From(settings, out var error);
            if (error is not null) throw new WorkspaceException(WorkspaceError.Invalid, error);
        }
        foreach (var folder in changes.Folders)
        {
            FolderManifest manifest;
            if (folder.IsRoot) manifest = candidate;
            else if (!candidate.FolderManifests.TryGetValue(folder.Path, out manifest!))
                manifest = candidate.FolderManifests[folder.Path] = new FolderManifest { Id = folder.Id };
            manifest.PinnedView = folder.PinnedView;
            manifest.Views = folder.Views.Count == 0 ? null : new(folder.Views, StringComparer.Ordinal);
            manifest.GridFolder = null;
            manifest.ItemOrder = folder.Children.Select(child => child.Id).ToArray();
        }
        foreach (var document in changes.Documents)
        {
            if (!candidate.Documents.TryGetValue(document.Id, out var metadata))
            {
                var path = FreePath(files, candidate, document.Path);
                var bytes = Encode($"---\nwriter_id: {document.Id}\n---\n\n", document.Body ?? "");
                await files.WriteAsync(path, bytes, overwrite: false);
                metadata = candidate.Documents[document.Id] = new DocumentMetadata { Path = path, LastKnownHash = Hash(bytes) };
            }
            else
            {
                if (metadata.Path != document.Path)
                {
                    if (!IsDocument(document.Path)) throw new WorkspaceException(WorkspaceError.Invalid, "Use a .md, .markdown, or .txt file name.");
                    if (files.Exists(document.Path)) throw new WorkspaceException(WorkspaceError.Conflict, "A file already exists at that path.");
                    Check(Hash(await ReadAsync(files, metadata.Path)), document.Revision);
                    files.Move(metadata.Path, document.Path);
                    metadata.Path = document.Path;
                }
                if (document.Body is { } body)
                {
                    var current = await ReadAsync(files, metadata.Path);
                    Check(Hash(current), document.Revision);
                    var bytes = Encode(Split(Decode(current)).Prefix, body);
                    if (!bytes.AsSpan().SequenceEqual(current))
                    {
                        await files.WriteAsync(metadata.Path, bytes);
                        metadata.LastKnownHash = Hash(bytes);
                    }
                }
            }
            metadata.Title = document.Title;
            metadata.Synopsis = document.Synopsis;
            metadata.Notes = document.Notes;
            metadata.Status = document.Status;
            metadata.WordGoal = document.WordGoal;
        }
        SaveLinks(candidate.Links, changes.Documents);
        await project.Manifests.WriteAsync(candidate, persistedRevision);
        return await LoadAsync(project);
    }

    private async Task<ProjectData> LoadAsync(DiskProject project)
    {
        var files = project.Files;
        var (persisted, persistedRevision) = await project.Manifests.ReadAsync();
        var candidate = persisted?.Clone() ?? NewManifest(project.Name);
        var scanned = new Dictionary<string, ScannedFile>(StringComparer.Ordinal);
        var warnings = new List<string>();
        var knownPaths = candidate.Documents.ToDictionary(x => x.Key, x => x.Value.Path, StringComparer.Ordinal);
        var paths = files.EnumerateDocuments().Order(StringComparer.Ordinal).ToArray();
        var livePaths = paths.ToHashSet(StringComparer.Ordinal);
        foreach (var relativePath in paths)
        {
            byte[] bytes;
            try { bytes = await files.ReadAsync(relativePath); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            catch (IOException) { throw new WorkspaceException(WorkspaceError.Unavailable, "A document is still being written. Odysseum will retry shortly."); }
            catch (WorkspaceException ex) { warnings.Add($"{relativePath}: {ex.Message}"); continue; }
            string text;
            try { text = Decode(bytes); }
            catch (DecoderFallbackException) { warnings.Add($"{relativePath} is not UTF-8 and was left untouched."); continue; }
            var (_, body, embeddedId) = Split(text);
            var hash = Hash(bytes);
            var id = embeddedId ?? knownPaths.FirstOrDefault(x => x.Value == relativePath).Key;
            if (id is null)
            {
                var matches = candidate.Documents.Where(x => !livePaths.Contains(x.Value.Path)
                    && x.Value.LastKnownHash == hash && !scanned.ContainsKey(x.Key)).ToArray();
                if (matches.Length == 1 && paths.Count(p => p != relativePath && !knownPaths.ContainsValue(p)) == 0)
                    id = matches[0].Key;
            }
            id ??= Guid.NewGuid().ToString();
            if (scanned.ContainsKey(id))
                throw new WorkspaceException(WorkspaceError.Conflict, $"Two files have the same writer_id. Give the copy a new ID: {relativePath}");
            scanned[id] = new(id, relativePath, hash, Modified(files, relativePath), CountWords(body));
            if (!candidate.Documents.TryGetValue(id, out var metadata))
            {
                metadata = new DocumentMetadata
                {
                    Title = IsFolderDocument(relativePath) ? Path.GetFileName(Path.GetDirectoryName(relativePath)!) : Path.GetFileNameWithoutExtension(relativePath),
                    WordGoal = IsFolderDocument(relativePath) ? 0 : candidate.Settings.DefaultSceneWordGoal,
                };
                candidate.Documents[id] = metadata;
            }
            metadata.Path = relativePath;
            metadata.LastKnownHash = hash;
        }
        // Every folder gets its own hidden document. This is the only write a load makes to the files.
        foreach (var folder in files.EnumerateFolders())
        {
            var own = FolderDocumentPath(folder);
            if (livePaths.Contains(own) || files.Exists(own)) continue;
            var id = Guid.NewGuid().ToString();
            var bytes = Encode($"---\nwriter_id: {id}\n---\n\n", "");
            await files.WriteAsync(own, bytes, overwrite: false);
            candidate.Documents[id] = new DocumentMetadata { Path = own, Title = Path.GetFileName(folder), WordGoal = 0, LastKnownHash = Hash(bytes) };
            scanned[id] = new(id, own, Hash(bytes), Modified(files, own), 0);
        }
        var revision = await project.Manifests.WriteAsync(candidate, persistedRevision);
        _ids[candidate.Id] = project.Name;
        project.Paths = scanned.ToDictionary(pair => pair.Key, pair => pair.Value.Path, StringComparer.Ordinal);
        var root = BuildTree(project.Name, candidate, scanned);
        return new ProjectData(candidate.Id, candidate.Settings.Clone(), revision, root, warnings.Count > 0 ? string.Join(" ", warnings) : null);
    }

    private static Folder BuildTree(string name, ProjectManifest manifest, Dictionary<string, ScannedFile> scanned)
    {
        // Links to documents whose files are gone stay in links.json, so a restored file gets its links back.
        var links = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var notes = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var link in manifest.Links.Links)
        {
            if (!scanned.ContainsKey(link.ItemA) || !scanned.ContainsKey(link.ItemB)) continue;
            foreach (var (from, to) in new[] { (link.ItemA, link.ItemB), (link.ItemB, link.ItemA) })
            {
                (links.TryGetValue(from, out var list) ? list : links[from] = []).Add(to);
                if (link.Note.Length > 0) (notes.TryGetValue(from, out var map) ? map : notes[from] = new(StringComparer.Ordinal))[to] = link.Note;
            }
        }
        var documents = new Dictionary<string, Document>(StringComparer.Ordinal);
        foreach (var file in scanned.Values)
        {
            var metadata = manifest.Documents[file.Id];
            documents[file.Id] = new Document(file.Id, Path.GetFileName(file.Path), metadata.Title, metadata.Synopsis, metadata.Notes, metadata.Status,
                metadata.WordGoal, links.GetValueOrDefault(file.Id)?.ToArray() ?? [], notes.GetValueOrDefault(file.Id) ?? new(StringComparer.Ordinal),
                file.Revision, file.Modified, file.WordCount);
        }
        var byFolder = scanned.Values.GroupBy(file => Item.ParentPath(file.Path), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var folderPaths = manifest.FolderManifests.Keys.ToArray();

        Folder Build(string path, FolderManifest local, string folderName)
        {
            var subfolders = folderPaths.Where(p => Item.ParentPath(p) == path)
                .Select(p => Build(p, manifest.FolderManifests[p], Path.GetFileName(p))).ToDictionary(f => f.Id, StringComparer.Ordinal);
            var files = byFolder.GetValueOrDefault(path) ?? [];
            var own = files.FirstOrDefault(f => IsFolderDocument(f.Path));
            var docs = files.Where(f => !IsFolderDocument(f.Path)).ToDictionary(f => f.Id, f => documents[f.Id], StringComparer.Ordinal);
            var children = new List<Item>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in local.ItemOrder)
            {
                if (!seen.Add(id)) continue;
                if (subfolders.TryGetValue(id, out var sub)) children.Add(sub);
                else if (docs.TryGetValue(id, out var doc)) children.Add(doc);
            }
            children.AddRange(subfolders.Values.Where(f => !seen.Contains(f.Id))
                .OrderBy(f => path == "" ? DefaultFolders.Rank(f.Name) : 0).ThenBy(f => f.Name, StringComparer.Ordinal));
            children.AddRange(docs.Values.Where(d => !seen.Contains(d.Id)).OrderBy(d => d.Name, StringComparer.Ordinal));
            return new Folder(local.Id, folderName, local.PinnedView, local.Views, children, own is null ? null : documents[own.Id]);
        }

        return Build("", manifest, name);
    }

    /// <summary>Brings links.json in line with the saved documents. The editor saves both ends of every link it changes,
    /// so the links of a saved document are complete: a link it no longer lists is removed, a new one is added at the end,
    /// and a kept one stays in place with the saved note.</summary>
    private static void SaveLinks(LinkManifest manifest, IReadOnlyList<Document> saved)
    {
        foreach (var document in saved)
        {
            manifest.Links.RemoveAll(link => link.Joins(document.Id) && !document.Links.Contains(link.Other(document.Id), StringComparer.Ordinal));
            foreach (var other in document.Links)
            {
                var note = document.LinkNotes.GetValueOrDefault(other) ?? "";
                if (manifest.Links.FirstOrDefault(link => link.Joins(document.Id, other)) is { } existing) existing.Note = note;
                else manifest.Links.Add(new LinkEntry { ItemA = document.Id, ItemB = other, Note = note });
            }
        }
    }

    private static Dictionary<string, string> FolderPaths(ProjectManifest manifest)
    {
        var paths = manifest.FolderManifests.ToDictionary(pair => pair.Value.Id, pair => pair.Key, StringComparer.Ordinal);
        paths[manifest.Id] = "";
        return paths;
    }

    private static string FreePath(FileManager files, ProjectManifest candidate, string wanted)
    {
        var parent = Item.ParentPath(wanted);
        var stem = Path.GetFileNameWithoutExtension(wanted);
        var extension = Path.GetExtension(wanted);
        var taken = candidate.Documents.Values.Select(d => d.Path).ToHashSet(StringComparer.Ordinal);
        var path = wanted;
        var suffix = 2;
        while (files.Exists(path) || taken.Contains(path)) path = Item.Join(parent, $"{stem}-{suffix++}{extension}");
        return path;
    }

    private static async Task<byte[]> ReadAsync(FileManager files, string path)
    {
        try { return await files.ReadAsync(path); }
        catch (FileNotFoundException) { throw MissingDocument(); }
        catch (DirectoryNotFoundException) { throw MissingDocument(); }
    }

    private static DateTimeOffset Modified(FileManager files, string path) =>
        new(DateTime.SpecifyKind(files.LastModified(path), DateTimeKind.Utc));

    private static WorkspaceException MissingDocument() =>
        new(WorkspaceError.NotFound, "This document was removed or moved outside the workspace. Your browser draft is still available.");

    private static ProjectManifest NewManifest(string name) => new() { Settings = new ProjectSettings { Title = name } };

    private DiskProject Get(ProjectBranch branch)
    {
        if (!branch.IsMain) throw new WorkspaceException(WorkspaceError.Invalid, "Only the main branch exists.");
        var name = ProjectNames.Validate(branch.Project);
        if (!_workspace.FolderExists(name))
        {
            _projects.TryRemove(name, out _);
            throw new WorkspaceException(WorkspaceError.NotFound, "That project no longer exists in the workspace.");
        }
        return _projects.GetOrAdd(name, key => new DiskProject(key, new FileManager(Path.Combine(Root, key), ownWrites)));
    }

    /// <summary>The ID and title from <c>project.json</c> alone. A project that was never opened has no ID yet.</summary>
    private async Task<ProjectInfo> DescribeAsync(string name)
    {
        var title = name;
        var id = "";
        var files = new FileManager(Path.Combine(Root, name));
        try
        {
            if (await new ManifestFiles(files).ReadRootAsync() is { } manifest)
            {
                title = manifest.Settings.Title;
                id = manifest.Id;
            }
            else if (files.Exists(".writer/project.json", metadata: true))
            {
                var legacy = JsonNode.Parse(await files.ReadAsync(".writer/project.json", metadata: true));
                title = Text(legacy?["settings"]?["title"]) ?? Text(legacy?["title"]) ?? title;
                id = Text(legacy?["id"]) ?? id;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or WorkspaceException or JsonException) { }
        if (id.Length > 0) _ids[id] = name;
        return new(name, title, id, new DateTimeOffset(DateTime.SpecifyKind(_workspace.LastModified(name), DateTimeKind.Utc)));
    }

    private async Task<string?> IdOfAsync(string name)
    {
        if (!_workspace.FolderExists(name)) return null;
        try { return (await new ManifestFiles(new FileManager(Path.Combine(Root, name))).ReadRootAsync())?.Id; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or WorkspaceException) { return null; }
    }

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
