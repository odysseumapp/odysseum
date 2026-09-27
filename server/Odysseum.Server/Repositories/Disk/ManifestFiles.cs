using Odysseum.Abstractions.Exceptions;
using Odysseum.Server.Repositories.Files;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Odysseum.Server.Repositories.Manifests;
using Odysseum.Server.Services.Views;
using Odysseum.Server.Settings;

namespace Odysseum.Server.Repositories.Disk;

internal sealed class ManifestFiles(IFileManager files)
{
    private const string ManifestPath = ".odysseum/project.json";
    private const string LinksPath = ".odysseum/links.json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { WriteIndented = true, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
    private static string FolderPath(string folder) => folder + "/.odysseum/folder.json";
    private static string Parent(string path) => Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "";

    private async Task<Dictionary<string, byte[]>> ReadFilesAsync()
    {
        if (new ManifestTransaction(files).Pending)
            throw new WorkspaceException(WorkspaceError.Unavailable, "A manifest update needs recovery. Reopen the project before saving.");
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (files.Exists(ManifestPath, metadata: true)) result[ManifestPath] = await files.ReadAsync(ManifestPath, metadata: true);
        if (files.Exists(LinksPath, metadata: true)) result[LinksPath] = await files.ReadAsync(LinksPath, metadata: true);
        foreach (var folder in files.EnumerateFolders())
        {
            var path = FolderPath(folder);
            if (files.Exists(path, metadata: true)) result[path] = await files.ReadAsync(path, metadata: true);
        }
        return result;
    }

    private static string Revision(Dictionary<string, byte[]> contents) => contents.Count == 0 ? ""
        : ContentRevision.Hash(Encoding.UTF8.GetBytes(string.Join('\n', contents.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => p.Key + ":" + ContentRevision.Hash(p.Value)))));

    /// <summary>Only <c>project.json</c>: the project's ID and settings, without the folder manifests. Null when it is missing.</summary>
    public async Task<ProjectManifest?> ReadRootAsync() =>
        files.Exists(ManifestPath, metadata: true) ? Parse<ProjectManifest>(await files.ReadAsync(ManifestPath, metadata: true), ManifestPath, root: true) : null;

    public async Task<(ProjectManifest? Manifest, string Revision)> ReadAsync()
    {
        var contents = await ReadFilesAsync();
        if (!contents.TryGetValue(ManifestPath, out var bytes))
        {
            if (contents.Keys.Any(path => path != LinksPath)) throw Invalid(ManifestPath);
            return (null, "");
        }
        var manifest = Parse<ProjectManifest>(bytes, ManifestPath, root: true);
        if (contents.TryGetValue(LinksPath, out var links)) manifest.Links = ParseLinks(links);
        if (manifest.Version == 1)
        {
            MoveLegacyLinks(manifest, manifest.Documents);
            return (manifest, Revision(contents));
        }
        var folderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { manifest.Id };
        foreach (var (path, content) in contents.Where(p => p.Key != ManifestPath && p.Key != LinksPath))
        {
            var folder = path[..^"/.odysseum/folder.json".Length];
            var local = Parse<FolderManifest>(content, path);
            if (!folderIds.Add(local.Id)) throw new WorkspaceException(WorkspaceError.Conflict, $"Two folders have the same manifest ID: {folder}");
            manifest.FolderManifests[folder] = local;
            foreach (var (id, metadata) in local.Documents)
            {
                var document = metadata.Clone();
                document.Path = folder + "/" + metadata.Path;
                if (!manifest.Documents.TryAdd(id, document))
                    throw new WorkspaceException(WorkspaceError.Conflict, $"Two folder manifests track the same document: {document.Path}");
            }
        }
        MoveLegacyLinks(manifest, manifest.Documents);
        return (manifest, Revision(contents));
    }

    public async Task<string> WriteAsync(ProjectManifest candidate, string expectedRevision)
    {
        var before = await ReadFilesAsync();
        if (Revision(before) != expectedRevision) throw Changed();
        var root = candidate.Clone();
        root.Version = 2;
        root.Documents = [];
        var folders = files.EnumerateFolders().ToDictionary(path => path,
            path => candidate.FolderManifests.TryGetValue(path, out var known) ? known.CloneFolder() : new FolderManifest(), StringComparer.Ordinal);
        var owners = new Dictionary<string, FolderManifest>(folders, StringComparer.Ordinal) { [""] = root };
        foreach (var owner in owners.Values) owner.Documents = [];
        foreach (var (id, metadata) in candidate.Documents)
        {
            var parent = Parent(metadata.Path);
            if (!owners.TryGetValue(parent, out var owner)) continue;
            var local = metadata.Clone();
            local.Path = Path.GetFileName(metadata.Path);
            local.Links = null;
            local.LinkNotes = null;
            owner.Documents[id] = local;
        }
        foreach (var (parent, owner) in owners)
        {
            var children = folders.Where(p => Parent(p.Key) == parent).ToArray();
            var previous = owner.Folders;
            owner.Folders = [];
            foreach (var (path, child) in children)
            {
                var entry = previous.TryGetValue(child.Id, out var existing) ? existing.Clone() : new FolderEntry();
                entry.Path = Path.GetFileName(path);
                owner.Folders[child.Id] = entry;
            }
            MigrateItemOrder(owner);
        }
        var after = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (folder, manifest) in folders) after[FolderPath(folder)] = Serialize(manifest);
        after[ManifestPath] = Serialize(root);
        if (candidate.Links.Links.Count > 0 || before.ContainsKey(LinksPath)) after[LinksPath] = Serialize(candidate.Links);
        var changes = after.Where(p => !before.TryGetValue(p.Key, out var old) || !old.AsSpan().SequenceEqual(p.Value))
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        if (before.TryGetValue(ManifestPath, out var legacy) && Parse<ProjectManifest>(legacy, ManifestPath, root: true).Version == 1
            && !files.Exists(".odysseum/project.v1.json", metadata: true))
            await files.WriteAsync(".odysseum/project.v1.json", legacy, overwrite: false, metadata: true);
        if (Revision(await ReadFilesAsync()) != expectedRevision) throw Changed();
        await new ManifestTransaction(files).CommitAsync(changes, before);
        candidate.Version = 2;
        candidate.Folders = root.Folders;
        candidate.FolderManifests = folders;
        foreach (var document in candidate.Documents.Values.Concat(folders.Values.SelectMany(f => f.Documents.Values))) document.LegacyOrder = null;
        foreach (var entry in candidate.Folders.Values.Concat(folders.Values.SelectMany(f => f.Folders.Values))) entry.LegacyOrder = null;
        return Revision(after);
    }

    private static byte[] Serialize<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        if (bytes.Length > FileManager.MaxFileBytes) throw new WorkspaceException(WorkspaceError.TooLarge, "Folder metadata exceeds the 4 MB limit.");
        return bytes;
    }

    private static LinkManifest ParseLinks(byte[] bytes)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<LinkManifest>(bytes, Json);
            if (manifest is null || manifest.Version != 1 || manifest.Links is null) throw new JsonException();
            var kept = new List<LinkEntry>();
            foreach (var link in manifest.Links)
            {
                if (link is null || !Guid.TryParseExact(link.ItemA, "D", out _) || !Guid.TryParseExact(link.ItemB, "D", out _) || link.ItemA == link.ItemB)
                    throw new JsonException();
                link.Note ??= "";
                if (!kept.Any(other => other.Joins(link.ItemA, link.ItemB))) kept.Add(link);
            }
            manifest.Links = kept;
            return manifest;
        }
        catch (JsonException) { throw Invalid(LinksPath); }
    }

    /// <summary>Earlier releases kept each link on both documents. Adds those links to <c>links.json</c> once, with the
    /// note from either end, and clears them from the documents.</summary>
    private static void MoveLegacyLinks(ProjectManifest manifest, Dictionary<string, DocumentMetadata> documents)
    {
        foreach (var (id, document) in documents)
        {
            foreach (var target in document.Links ?? [])
            {
                if (target == id || manifest.Links.Links.Any(link => link.Joins(id, target))) continue;
                var note = document.LinkNotes?.GetValueOrDefault(target);
                if (string.IsNullOrEmpty(note) && documents.TryGetValue(target, out var other)) note = other.LinkNotes?.GetValueOrDefault(id);
                manifest.Links.Links.Add(new LinkEntry { ItemA = id, ItemB = target, Note = note ?? "" });
            }
        }
        foreach (var document in documents.Values)
        {
            document.Links = null;
            document.LinkNotes = null;
        }
    }

    private static T Parse<T>(byte[] bytes, string path, bool root = false) where T : FolderManifest
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<T>(bytes, Json);
            if (manifest is null || (root ? manifest.Version is not (1 or 2) : manifest.Version != 1)
                || !Guid.TryParse(manifest.Id, out _) || manifest.Documents is null || manifest.Folders is null)
                throw new JsonException();
            foreach (var (id, document) in manifest.Documents)
            {
                if (!Guid.TryParseExact(id, "D", out _) || document is null
                    || document.Path is null || document.Title is null || document.Synopsis is null || document.Notes is null
                    || !Enum.IsDefined(document.Status) || document.WordGoal is < 0 or > 10000000
                    || !SafePath(document.Path, root && manifest.Version == 1))
                    throw new JsonException();
                document.LegacyOrder = TakeOrder(document.Extra);
                document.Links ??= [];
                MergeLegacyLinks(document);
                if (document.Links.Any(id => !Guid.TryParseExact(id, "D", out _)) || document.Links.Distinct().Count() != document.Links.Count) throw new JsonException();
                document.LinkNotes ??= [];
                if (document.LinkNotes.Any(note => !Guid.TryParseExact(note.Key, "D", out _) || note.Value is null)) throw new JsonException();
            }
            if (manifest.GridFolder is not null && !Guid.TryParseExact(manifest.GridFolder, "D", out _)) throw new JsonException();
            MergeLegacyLayout(manifest);
            if ((manifest.PinnedView is not null && !ViewNames.IsValid(manifest.PinnedView))
                || (manifest.Views?.Any(view => !ViewNames.IsValid(view.Key) || view.Value.ValueKind != JsonValueKind.Object) ?? false)
                || manifest.ItemOrder is null
                || manifest.ItemOrder.Any(string.IsNullOrWhiteSpace)
                || manifest.ItemOrder.Distinct().Count() != manifest.ItemOrder.Length)
                throw new JsonException();
            var localPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (id, folder) in manifest.Folders)
            {
                if (!Guid.TryParseExact(id, "D", out _) || folder is null
                    || !SafePath(folder.Path, false) || !localPaths.Add(folder.Path)) throw new JsonException();
                folder.LegacyOrder = TakeOrder(folder.Extra);
                if (folder.Extra is { Count: 0 }) folder.Extra = null;
            }
            if (manifest is ProjectManifest project)
            {
                project.Settings ??= new ProjectSettings();
                if (project.Extra is not null)
                {
                    if (project.Extra.Remove("title", out var title) && title.ValueKind == JsonValueKind.String)
                        project.Settings.Title = title.GetString() ?? project.Settings.Title;
                    if (project.Extra.Remove("wordGoal", out var goal) && goal.ValueKind == JsonValueKind.Number && goal.TryGetInt32(out var value))
                        project.Settings.WordGoal = value;
                    if (project.Extra.Count == 0) project.Extra = null;
                }
                if (!ProjectSettings.TryValidate(project.Settings, out _)) throw new JsonException();
            }
            return manifest;
        }
        catch (JsonException) { throw Invalid(path); }
    }

    private static void MigrateItemOrder(FolderManifest owner)
    {
        if (!owner.ItemOrder.Any(key => key.StartsWith("folder:", StringComparison.Ordinal))
            && owner.Documents.Values.All(d => d.LegacyOrder is null) && owner.Folders.Values.All(f => f.LegacyOrder is null)) return;
        var byName = owner.Folders.ToDictionary(pair => pair.Value.Path, pair => pair.Key, StringComparer.Ordinal);
        var listed = owner.ItemOrder
            .Select(key => key.StartsWith("folder:", StringComparison.Ordinal) ? byName.GetValueOrDefault(key["folder:".Length..]) : key)
            .OfType<string>().Distinct().ToList();
        var seen = listed.ToHashSet(StringComparer.Ordinal);
        var folders = owner.Folders.Where(pair => !seen.Contains(pair.Key))
            .OrderBy(pair => pair.Value.LegacyOrder ?? double.MaxValue).ThenBy(pair => pair.Value.Path, StringComparer.Ordinal).Select(pair => pair.Key);
        var documents = owner.Documents.Where(pair => !seen.Contains(pair.Key) && !pair.Value.Path.StartsWith('.'))
            .OrderBy(pair => pair.Value.LegacyOrder ?? double.MaxValue).ThenBy(pair => pair.Value.Path, StringComparer.Ordinal).Select(pair => pair.Key);
        owner.ItemOrder = [.. listed, .. folders, .. documents];
    }

    private static double? TakeOrder(Dictionary<string, JsonElement>? extra)
    {
        if (extra is null || !extra.Remove("order", out var value)) return null;
        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var order) && double.IsFinite(order) ? order : null;
    }

    private static List<string> LegacyIds(Dictionary<string, JsonElement>? extra, string key)
    {
        if (extra is null || !extra.Remove(key, out var value) || value.ValueKind != JsonValueKind.Array) return [];
        return value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToList();
    }
    private static void MergeLegacyLinks(DocumentMetadata document)
    {
        document.Links ??= [];
        foreach (var key in new[] { "characters", "locations", "threads" })
            foreach (var id in LegacyIds(document.Extra, key)) if (!document.Links.Contains(id)) document.Links.Add(id);
        if (document.Extra is { Count: 0 }) document.Extra = null;
    }
    private static void MergeLegacyLayout(FolderManifest manifest)
    {
        foreach (var key in new[] { "threads", "threadAxis", "positions" }) manifest.Extra?.Remove(key);
        if (manifest.PinnedView == "threads") manifest.PinnedView = "grid";
        // Earlier releases kept the grid's column folder in its own field.
        if (manifest.GridFolder is { } columnFolder)
        {
            manifest.Views ??= new(StringComparer.Ordinal);
            if (!manifest.Views.ContainsKey("grid"))
                manifest.Views["grid"] = JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["columnFolder"] = columnFolder });
            manifest.GridFolder = null;
        }
        if (manifest.Views is { Count: 0 }) manifest.Views = null;
        if (manifest.Extra is { Count: 0 }) manifest.Extra = null;
    }

    private static bool SafePath(string? path, bool nested)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains('\\') || path.Contains(':') || (!nested && path.Contains('/'))) return false;
        var parts = path.Split('/');
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (string.IsNullOrWhiteSpace(part) || part.EndsWith('.') || part.EndsWith(' ') || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
            if (part.StartsWith('.') && !(i == parts.Length - 1 && part.Length > 4 && part.EndsWith(".md", StringComparison.OrdinalIgnoreCase))) return false;
        }
        return true;
    }
    private static WorkspaceException Invalid(string path) => new(WorkspaceError.Corrupt, $"The metadata is invalid or uses an unsupported version. Fix {path} before saving.");
    private static WorkspaceException Changed() => new(WorkspaceError.Conflict, "Project metadata changed on disk. Refresh before saving again.");
}
