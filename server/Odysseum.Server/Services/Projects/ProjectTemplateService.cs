using Odysseum.Server.Services.Storage;
using Odysseum.Server.Services.Templates;
using Odysseum.Server.Settings;
using static Odysseum.Server.Services.Documents.DocumentRules;
using static Odysseum.Server.Services.Documents.MarkdownDocumentCodec;

namespace Odysseum.Server.Services.Projects;

internal sealed class ProjectTemplateService(ProjectState state, ProjectFileStore files)
{
    private static string Parent(string path) => Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "";

    public ProjectTemplate Capture(string name)
    {
        var manifest = state.Manifest;
        var paths = state.Documents.ToDictionary(pair => pair.Key, pair => pair.Value.Path, StringComparer.Ordinal);
        var template = new ProjectTemplate
        {
            Name = name,
            Settings = new() { WordGoal = manifest.Settings.WordGoal, DefaultSceneWordGoal = manifest.Settings.DefaultSceneWordGoal },
        };
        foreach (var (path, folder) in manifest.FolderManifests.OrderBy(pair => pair.Key, StringComparer.Ordinal).Prepend(new("", manifest)))
        {
            template.Folders.Add(new()
            {
                Path = path,
                PinnedView = folder.PinnedView,
                ItemOrder = [.. folder.ItemOrder.Select(key => key.StartsWith("folder:") ? key
                    : paths.TryGetValue(key, out var document) ? "document:" + Path.GetFileName(document) : null).OfType<string>()],
                GridFolder = folder.GridFolder is null ? null : folder.GridFolder == manifest.Id ? ""
                    : manifest.FolderManifests.FirstOrDefault(pair => pair.Value.Id == folder.GridFolder).Key,
            });
        }
        foreach (var document in state.Documents.Values.Where(x => !IsFolderDocument(x.Path)).OrderBy(x => manifest.Documents[x.Id].Order).ThenBy(x => x.Path, StringComparer.Ordinal))
            template.Documents.Add(new() { Path = document.Path, Title = manifest.Documents[document.Id].Title });
        return template;
    }

    public async Task<Dictionary<string, string>> WriteFilesAsync(ProjectTemplate template)
    {
        foreach (var path in template.Folders.Select(folder => folder.Path).Concat(template.Documents.Select(document => Parent(document.Path))).Where(path => path != ""))
        {
            var segments = path.Split('/');
            for (var depth = 1; depth <= segments.Length; depth++) files.CreateFolder(string.Join('/', segments[..depth]));
        }
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var document in template.Documents)
        {
            if (files.Exists(document.Path)) continue;
            var id = Guid.NewGuid().ToString();
            await files.WriteAsync(document.Path, Encode($"---\nwriter_id: {id}\n---\n\n", document.Content ?? ""), overwrite: false);
            ids[document.Path] = id;
        }
        return ids;
    }

    public async Task ApplyDetailsAsync(ProjectTemplate template, Dictionary<string, string> ids, ProjectSettings settings)
    {
        var candidate = state.Manifest.Clone();
        candidate.Settings = settings;
        var order = 0;
        foreach (var document in template.Documents)
        {
            if (!ids.TryGetValue(document.Path, out var id) || !candidate.Documents.TryGetValue(id, out var metadata)) continue;
            metadata.Title = document.Title;
            metadata.WordGoal = IsFolderDocument(document.Path) ? 0 : settings.DefaultSceneWordGoal;
            metadata.Order = order++;
        }
        var placed = ids.Values.ToHashSet(StringComparer.Ordinal);
        foreach (var metadata in candidate.Documents.Where(pair => !placed.Contains(pair.Key)).OrderBy(pair => pair.Value.Order).Select(pair => pair.Value))
            metadata.Order = order++;

        foreach (var folder in template.Folders)
        {
            var manifest = folder.Path == "" ? candidate : candidate.FolderManifests.GetValueOrDefault(folder.Path);
            if (manifest is null) continue;
            var child = (string name) => folder.Path == "" ? name : folder.Path + "/" + name;
            manifest.PinnedView = folder.PinnedView;
            manifest.ItemOrder = [.. folder.ItemOrder.Select(key =>
                key.StartsWith("folder:") ? (candidate.FolderManifests.ContainsKey(child(key["folder:".Length..])) ? key : null)
                : key.StartsWith("document:") ? ids.GetValueOrDefault(child(key["document:".Length..])) : null).OfType<string>().Distinct()];
            manifest.GridFolder = folder.GridFolder is null ? null : folder.GridFolder == "" ? candidate.Id
                : candidate.FolderManifests.GetValueOrDefault(folder.GridFolder)?.Id;
        }
        await state.CommitManifestAsync(candidate, state.Revision);
        state.PublishChanges();
    }
}
