using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Concurrent;
using Odysseum.Server.API.Models;
using Odysseum.Server.Services.Documents;
using Odysseum.Server.Services.Storage;
using Odysseum.Server.Services.Templates;
using Odysseum.Server.Settings;

namespace Odysseum.Server.Services;

public sealed class ProjectLibrary(string root, ProjectFactory factory, TemplateStore? templates = null) : IAsyncDisposable
{
    public static readonly string[] DefaultFolders = ["Manuscript", "Characters", "Locations", "Threads", "Notes", "Styles"];
    public static bool IsDefaultFolder(string path) => DefaultFolders.Contains(path, StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, Lazy<Task<ProjectHandle>>> _open =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly SemaphoreSlim _createGate = new(1, 1);
    public string Root { get; } = Path.GetFullPath(root);

    public async Task<IReadOnlyList<ProjectInfo>> ListAsync()
    {
        Directory.CreateDirectory(Root);
        var projects = new List<ProjectInfo>();
        foreach (var directory in Directory.EnumerateDirectories(Root))
        {
            var slug = Path.GetFileName(directory);
            if (slug.StartsWith('.') || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
            projects.Add(await DescribeAsync(slug, directory));
        }
        return projects.OrderBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.Slug, StringComparer.Ordinal).ToArray();
    }

    public async Task<ProjectHandle> OpenAsync(string slug)
    {
        slug = ValidateSlug(slug);
        var path = Path.Combine(Root, slug);
        if (!Directory.Exists(path))
        {
            if (_open.TryRemove(slug, out var stale) && stale.IsValueCreated && stale.Value.IsCompletedSuccessfully)
                await stale.Value.Result.DisposeAsync();
            throw new WorkspaceException(404, "That project no longer exists in the workspace.");
        }
        var lazy = _open.GetOrAdd(slug, key => new Lazy<Task<ProjectHandle>>(() => factory.OpenAsync(key, path)));
        try { return await lazy.Value; }
        catch
        {
            _open.TryRemove(new KeyValuePair<string, Lazy<Task<ProjectHandle>>>(slug, lazy));
            throw;
        }
    }

    public async Task<ProjectInfo> CreateAsync(CreateProjectRequest request)
    {
        var title = DocumentRules.ValidateTitle(request.Title);
        var stem = DocumentRules.FileName(title);
        var template = templates?.Get(string.IsNullOrWhiteSpace(request.Template) ? TemplateStore.DefaultName : request.Template)
            ?? (string.IsNullOrWhiteSpace(request.Template) || TemplateStore.IsDefault(request.Template) ? TemplateStore.Default()
                : throw new WorkspaceException(404, $"There is no project template called '{request.Template}'."));
        string slug;
        await _createGate.WaitAsync();
        try
        {
            Directory.CreateDirectory(Root);
            slug = stem;
            var suffix = 2;
            while (Directory.Exists(Path.Combine(Root, slug)) || File.Exists(Path.Combine(Root, slug))) slug = $"{stem}-{suffix++}";
            Directory.CreateDirectory(Path.Combine(Root, slug));
        }
        finally { _createGate.Release(); }
        var handle = await OpenAsync(slug);
        var settings = new ProjectSettings
        {
            Title = title, WordGoal = request.WordGoal ?? template.Settings.WordGoal, DefaultSceneWordGoal = template.Settings.DefaultSceneWordGoal,
        };
        await handle.Services.ApplyTemplateAsync(template, settings);
        return await DescribeAsync(slug, Path.Combine(Root, slug));
    }

    public async Task<ProjectServices> OpenServicesAsync(string slug) => (await OpenAsync(slug)).Services;

    public static string ValidateSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug.Length > 200 || slug is "." or ".." || slug.StartsWith('.')
            || slug.EndsWith('.') || slug.EndsWith(' ') || slug.Contains('/') || slug.Contains('\\')
            || slug.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new WorkspaceException(400, "That project name is not allowed.");
        return slug;
    }

    private static async Task<ProjectInfo> DescribeAsync(string slug, string directory)
    {
        var title = slug;
        var id = "";
        try
        {
            var (manifest, _) = await new ProjectManifestStore(new ProjectFileStore(directory)).ReadAsync();
            if (manifest is not null)
            {
                title = manifest.Settings.Title;
                id = manifest.Id;
            }
            else if (File.Exists(Path.Combine(directory, ".writer", "project.json")))
            {
                var legacy = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(directory, ".writer", "project.json")));
                title = Text(legacy?["settings"]?["title"]) ?? Text(legacy?["title"]) ?? title;
                id = Text(legacy?["id"]) ?? id;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or WorkspaceException or JsonException)
        {  }
        return new(slug, title, id, Directory.GetLastWriteTimeUtc(directory));
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
