using Odysseum.Abstractions.Folders;
using Odysseum.Abstractions.Projects;
using Odysseum.Server.Repositories.Manifests;
using Odysseum.Server.Services;
using Odysseum.Server.Services.Projects;
using ProjectSettings = Odysseum.Server.Settings.ProjectSettings;

namespace Odysseum.Server.Models;

public sealed class Project : IProject
{
    private readonly Dictionary<string, Folder> _foldersById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Folder> _foldersByPath = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Document> _documents = new(StringComparer.Ordinal);

    internal Project(OpenProject open, ProjectManifest manifest, string revision, IReadOnlyDictionary<string, DiskDocument> disk, string? warning)
    {
        Open = open;
        Manifest = manifest;
        Revision = revision;
        Disk = disk;
        Warning = warning;
        RootFolder = new Folder(this, null, "", open.Slug, manifest);
        _foldersById[RootFolder.Id] = RootFolder;
        _foldersByPath[""] = RootFolder;
        foreach (var (path, local) in manifest.FolderManifests.OrderBy(pair => pair.Key.Count(c => c == '/')).ThenBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var parent = _foldersByPath.GetValueOrDefault(ParentPath(path)) ?? RootFolder;
            var folder = new Folder(this, parent, path, System.IO.Path.GetFileName(path), local);
            _foldersById[folder.Id] = folder;
            _foldersByPath[path] = folder;
            parent.Add(folder);
        }
        foreach (var file in disk.Values.OrderBy(d => d.Path, StringComparer.Ordinal))
        {
            if (!manifest.Documents.TryGetValue(file.Id, out var metadata)) continue;
            var folder = _foldersByPath.GetValueOrDefault(ParentPath(file.Path)) ?? RootFolder;
            var document = new Document(this, folder, file, metadata);
            _documents[file.Id] = document;
            folder.Add(document);
        }
        var reverse = Links.Reverse(manifest, _documents.Keys);
        foreach (var document in _documents.Values) document.Resolve(reverse.GetValueOrDefault(document.Id) ?? []);
        Folders = _foldersByPath.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value).ToArray();
        Documents = _documents.Values.OrderBy(d => d.Path, StringComparer.Ordinal).ToArray();
        LastModified = Documents.Count == 0 ? default : Documents.Max(d => d.Modified);
    }

    private Project(Project source, ProjectSettings settings)
    {
        Open = source.Open;
        Manifest = source.Manifest.Clone();
        Manifest.Settings = settings;
        Revision = source.Revision;
        Disk = source.Disk;
        Warning = source.Warning;
        RootFolder = source.RootFolder;
        Folders = source.Folders;
        Documents = source.Documents;
        LastModified = source.LastModified;
        _foldersById = source._foldersById;
        _foldersByPath = source._foldersByPath;
        _documents = source._documents;
    }

    internal OpenProject Open { get; }
    internal ProjectManifest Manifest { get; }
    internal IReadOnlyDictionary<string, DiskDocument> Disk { get; }
    internal ProjectSettings Settings => Manifest.Settings;
    public string Id => Manifest.Id;
    public string Title => Manifest.Settings.Title;
    public int WordGoal => Manifest.Settings.WordGoal;
    public int DefaultSceneWordGoal => Manifest.Settings.DefaultSceneWordGoal;
    public string Slug => Open.Slug;
    public string Revision { get; }
    public string? Warning { get; }
    public DateTimeOffset LastModified { get; }
    public IFolder Root => RootFolder;
    public Folder RootFolder { get; }
    public IReadOnlyList<Folder> Folders { get; }
    public IReadOnlyList<Document> Documents { get; }

    public Folder? Folder(string id) => _foldersById.GetValueOrDefault(id);
    public Folder? FolderAt(string path) => _foldersByPath.GetValueOrDefault(path.Trim('/'));
    public Document? Document(string id) => _documents.GetValueOrDefault(id);

    internal Project With(ProjectSettings settings) => new(this, settings);

    internal string Fingerprint() => Revision + "|" + Warning + "|" + string.Join(';',
        Disk.Values.OrderBy(x => x.Id).Select(x => x.Id + x.Path + x.Revision));

    internal static string ParentPath(string path) => System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "";
}
