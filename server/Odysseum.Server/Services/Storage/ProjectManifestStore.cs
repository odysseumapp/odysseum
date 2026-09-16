using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Odysseum.Server.Services.Storage.Models;
using Odysseum.Server.Settings;

namespace Odysseum.Server.Services.Storage;

/// <summary>Translates folder-owned manifests into the flat project view used by services and the API.</summary>
internal sealed class ProjectManifestStore(ProjectFileStore files)
{
    private const string ManifestPath = ".odysseum/project.json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { WriteIndented = true, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
    private static string FolderPath(string folder) => folder + "/.odysseum/folder.json";
    private static string Parent(string path) => Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "";

    private async Task<Dictionary<string, byte[]>> ReadFilesAsync()
    {
        if (new ManifestTransaction(files).Pending)
            throw new WorkspaceException(503, "A manifest update needs recovery. Reopen the project before saving.");
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (files.Exists(ManifestPath, metadata: true)) result[ManifestPath] = await files.ReadAsync(ManifestPath, metadata: true);
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

    public async Task<(ProjectManifest? Manifest, string Revision)> ReadAsync()
    {
        var contents = await ReadFilesAsync();
        if (!contents.TryGetValue(ManifestPath, out var bytes))
        {
            if (contents.Count != 0) throw Invalid(ManifestPath);
            return (null, "");
        }
        var manifest = Parse<ProjectManifest>(bytes, ManifestPath, root: true);
        if (manifest.Version == 1) return (manifest, Revision(contents));
        var folderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { manifest.Id };
        foreach (var (path, content) in contents.Where(p => p.Key != ManifestPath))
        {
            var folder = path[..^"/.odysseum/folder.json".Length];
            var local = Parse<FolderManifest>(content, path);
            if (!folderIds.Add(local.Id)) throw new WorkspaceException(409, $"Two folders have the same manifest ID: {folder}");
            manifest.FolderManifests[folder] = local;
            foreach (var (id, metadata) in local.Documents)
            {
                var document = metadata.Clone();
                document.Path = folder + "/" + metadata.Path;
                if (!manifest.Documents.TryAdd(id, document))
                    throw new WorkspaceException(409, $"Two folder manifests track the same document: {document.Path}");
            }
        }
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
            // A removed folder takes its metadata with it. Deleted files in surviving folders retain theirs.
            if (!owners.TryGetValue(parent, out var owner)) continue;
            var local = metadata.Clone();
            local.Path = Path.GetFileName(metadata.Path);
            owner.Documents[id] = local;
        }
        foreach (var (parent, owner) in owners)
        {
            var children = folders.Where(p => Parent(p.Key) == parent).ToArray();
            var previous = owner.Folders;
            owner.Folders = [];
            var nextOrder = previous.Count == 0 ? 0 : previous.Values.Max(x => x.Order) + 1;
            foreach (var (path, child) in children)
            {
                var entry = previous.TryGetValue(child.Id, out var existing) ? existing.Clone() : new FolderEntry { Order = nextOrder++ };
                entry.Path = Path.GetFileName(path);
                owner.Folders[child.Id] = entry;
            }
        }
        var after = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (folder, manifest) in folders) after[FolderPath(folder)] = Serialize(manifest);
        after[ManifestPath] = Serialize(root);
        var changes = after.Where(p => !before.TryGetValue(p.Key, out var old) || !old.AsSpan().SequenceEqual(p.Value))
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        if (before.TryGetValue(ManifestPath, out var legacy) && Parse<ProjectManifest>(legacy, ManifestPath, root: true).Version == 1
            && !files.Exists(".odysseum/project.v1.json", metadata: true))
            await files.WriteAsync(".odysseum/project.v1.json", legacy, overwrite: false, metadata: true);
        // Recheck the entire set after preparing the batch, including newly discovered folder manifests.
        if (Revision(await ReadFilesAsync()) != expectedRevision) throw Changed();
        await new ManifestTransaction(files).CommitAsync(changes, before);
        candidate.Version = 2;
        candidate.Folders = root.Folders;
        candidate.FolderManifests = folders;
        return Revision(after);
    }

    private static byte[] Serialize<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        if (bytes.Length > ProjectFileStore.MaxFileBytes) throw new WorkspaceException(413, "Folder metadata exceeds the 4 MB limit.");
        return bytes;
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
                if (!Guid.TryParseExact(id, "D", out _) || document is null || !double.IsFinite(document.Order)
                    || document.Path is null || document.Title is null || document.Synopsis is null || document.Notes is null
                    || !Enum.IsDefined(document.Status) || document.WordGoal is < 0 or > 10000000
                    || !SafePath(document.Path, root && manifest.Version == 1))
                    throw new JsonException();
                document.Links ??= [];
                MergeLegacyLinks(document);
                if (document.Links.Any(id => !Guid.TryParseExact(id, "D", out _)) || document.Links.Distinct().Count() != document.Links.Count) throw new JsonException();
            }
            MergeLegacyLayout(manifest);
            if (manifest.PinnedView is not (null or "write" or "board" or "outline" or "grid")
                || manifest.ItemOrder is null
                || manifest.ItemOrder.Any(string.IsNullOrWhiteSpace)
                || manifest.ItemOrder.Distinct().Count() != manifest.ItemOrder.Length
                || (manifest.GridFolder is not null && !Guid.TryParseExact(manifest.GridFolder, "D", out _)))
                throw new JsonException();
            // Removed-document metadata is retained by ID. A replacement may reuse its old filename.
            var localPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (id, folder) in manifest.Folders)
                if (!Guid.TryParseExact(id, "D", out _) || folder is null || !double.IsFinite(folder.Order)
                    || !SafePath(folder.Path, false) || !localPaths.Add(folder.Path)) throw new JsonException();
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

    private static List<string> LegacyIds(Dictionary<string, JsonElement>? extra, string key)
    {
        if (extra is null || !extra.Remove(key, out var value) || value.ValueKind != JsonValueKind.Array) return [];
        return value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToList();
    }
    /// <summary>Character, location and thread lists from earlier releases become plain links on the next write.</summary>
    private static void MergeLegacyLinks(DocumentMetadata document)
    {
        foreach (var key in new[] { "characters", "locations", "threads" })
            foreach (var id in LegacyIds(document.Extra, key)) if (!document.Links.Contains(id)) document.Links.Add(id);
        if (document.Extra is { Count: 0 }) document.Extra = null;
    }
    /// <summary>The Threads view of earlier releases becomes the grid; its stored rows and positions carried no meaning the grid keeps.</summary>
    private static void MergeLegacyLayout(FolderManifest manifest)
    {
        foreach (var key in new[] { "threads", "threadAxis", "positions" }) manifest.Extra?.Remove(key);
        if (manifest.PinnedView == "threads") manifest.PinnedView = "grid";
        if (manifest.Extra is { Count: 0 }) manifest.Extra = null;
    }

    private static bool SafePath(string? path, bool nested) => !string.IsNullOrWhiteSpace(path)
        && !Path.IsPathRooted(path) && !path.Contains('\\') && !path.Contains(':') && (nested || !path.Contains('/'))
        && path.Split('/').All(part => !string.IsNullOrWhiteSpace(part) && !part.StartsWith('.') && !part.EndsWith('.')
            && !part.EndsWith(' ') && part.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
    private static WorkspaceException Invalid(string path) => new(422, $"The metadata is invalid or uses an unsupported version. Fix {path} before saving.");
    private static WorkspaceException Changed() => new(409, "Project metadata changed on disk. Refresh before saving again.");
}
