using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Odysseum.Abstractions.Exceptions;
using Odysseum.Server.API.Models;
using Odysseum.Server.Models;
using Odysseum.Server.Repositories;
using Odysseum.Server.Repositories.Files;
using Odysseum.Server.Services.Documents;
using Odysseum.Server.Services.Templates;
using Odysseum.Server.Settings;

namespace Odysseum.Server.Services;

public sealed class ProjectLibrary(string root, ProjectFactory factory, ITemplateRepository? templates = null) : IAsyncDisposable
{
    public static readonly string[] DefaultFolders = ["Manuscript", "Characters", "Locations", "Threads", "Notes", "Styles"];
    public static bool IsDefaultFolder(string path) => DefaultFolders.Contains(path, StringComparer.OrdinalIgnoreCase);

    private readonly FileManager _files = new(root);
    private readonly ProjectTemplateService _templateService = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<ProjectHandle>>> _open =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly SemaphoreSlim _createGate = new(1, 1);
    public string Root => _files.Root;

    public event Action<Project>? ProjectChanged;
    public event Action<Document>? DocumentRemoved;
    public event Action<Folder>? FolderRemoved;

    public IEnumerable<string> Slugs()
    {
        _files.CreateFolder("");
        return _files.EnumerateFolders(recursive: false);
    }

    public bool Exists(string slug) => _files.FolderExists(ValidateSlug(slug));

    public async Task<IReadOnlyList<ProjectInfo>> ListAsync()
    {
        var projects = new List<ProjectInfo>();
        foreach (var slug in Slugs()) projects.Add(await DescribeAsync(slug));
        return projects.OrderBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.Slug, StringComparer.Ordinal).ToArray();
    }

    public async Task<ProjectHandle> OpenAsync(string slug)
    {
        slug = ValidateSlug(slug);
        if (!_files.FolderExists(slug))
        {
            if (_open.TryRemove(slug, out var stale) && stale.IsValueCreated && stale.Value.IsCompletedSuccessfully)
                await stale.Value.Result.DisposeAsync();
            throw new WorkspaceException(WorkspaceError.NotFound, "That project no longer exists in the workspace.");
        }
        var lazy = _open.GetOrAdd(slug, key => new Lazy<Task<ProjectHandle>>(async () =>
        {
            var handle = await factory.OpenAsync(key, Path.Combine(Root, key));
            handle.Project.Changed += project => ProjectChanged?.Invoke(project);
            handle.Project.DocumentsRemoved += documents => { foreach (var document in documents) DocumentRemoved?.Invoke(document); };
            handle.Project.FoldersRemoved += folders => { foreach (var folder in folders) FolderRemoved?.Invoke(folder); };
            return handle;
        }));
        try { return await lazy.Value; }
        catch
        {
            _open.TryRemove(new KeyValuePair<string, Lazy<Task<ProjectHandle>>>(slug, lazy));
            throw;
        }
    }

    public async Task<OpenProject> OpenProjectAsync(string slug) => (await OpenAsync(slug)).Project;

    public async Task<ProjectInfo> CreateAsync(CreateProjectRequest request)
    {
        var title = DocumentRules.ValidateTitle(request.Title);
        var stem = DocumentRules.FileName(title);
        var template = templates?.Get(string.IsNullOrWhiteSpace(request.Template) ? TemplateRepository.DefaultName : request.Template)
            ?? (string.IsNullOrWhiteSpace(request.Template) || TemplateRepository.IsDefault(request.Template) ? TemplateRepository.Default()
                : throw new WorkspaceException(WorkspaceError.NotFound, $"There is no project template called '{request.Template}'."));
        var settings = ProjectSettings.From(new ProjectSettings
        {
            Title = title, WordGoal = request.WordGoal ?? template.Settings.WordGoal, DefaultSceneWordGoal = template.Settings.DefaultSceneWordGoal,
        }, out var error);
        if (error is not null) throw new WorkspaceException(WorkspaceError.Invalid, error);
        string slug;
        await _createGate.WaitAsync();
        try
        {
            _files.CreateFolder("");
            slug = stem;
            var suffix = 2;
            while (_files.FolderExists(slug) || _files.Exists(slug)) slug = $"{stem}-{suffix++}";
            _files.CreateFolder(slug);
        }
        finally { _createGate.Release(); }
        var handle = await OpenAsync(slug);
        await _templateService.ApplyAsync(handle.Project, template, settings);
        return await DescribeAsync(slug);
    }

    public static string ValidateSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug.Length > 200 || slug is "." or ".." || slug.StartsWith('.')
            || slug.EndsWith('.') || slug.EndsWith(' ') || slug.Contains('/') || slug.Contains('\\')
            || slug.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new WorkspaceException(WorkspaceError.Invalid, "That project name is not allowed.");
        return slug;
    }

    private async Task<ProjectInfo> DescribeAsync(string slug)
    {
        var title = slug;
        var id = "";
        var files = new FileManager(Path.Combine(Root, slug));
        try
        {
            var (manifest, _) = await new ProjectManifestRepository(files).ReadAsync();
            if (manifest is not null)
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or WorkspaceException or JsonException)
        {  }
        return new(slug, title, id, _files.LastModified(slug));
    }

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    public async ValueTask DisposeAsync()
    {
        foreach (var entry in _open.Values)
        {
            if (!entry.IsValueCreated) continue;
            try { await (await entry.Value).DisposeAsync(); }
            catch (WorkspaceException) {  }
        }
        _open.Clear();
        _createGate.Dispose();
    }
}
