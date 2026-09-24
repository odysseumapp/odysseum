using Odysseum.Server.Models;
using Odysseum.Server.Repositories;
using Odysseum.Server.Settings;
using static Odysseum.Server.Services.Documents.DocumentRules;
using static Odysseum.Server.Services.Documents.MarkdownDocumentCodec;

namespace Odysseum.Server.Services.Templates;

public sealed class ProjectTemplateService
{
    public ProjectTemplate Capture(Project project, string name)
    {
        var template = new ProjectTemplate
        {
            Name = name,
            Settings = new() { WordGoal = project.WordGoal, DefaultSceneWordGoal = project.DefaultSceneWordGoal },
        };
        foreach (var folder in project.Folders)
        {
            template.Folders.Add(new()
            {
                Path = folder.Path,
                PinnedView = folder.PinnedView?.ToString().ToLowerInvariant(),
                ItemOrder = [.. OrderService.Children(folder).Select(child => child.Folder is Folder sub ? "folder:" + sub.Name : "document:" + ((Document)child.Document!).FileName)],
                GridFolder = folder.GridFolderId is null ? null : project.Folder(folder.GridFolderId)?.Path,
            });
        }
        foreach (var document in OrderService.Walk(project.RootFolder).Where(d => !d.IsFolderDocument))
            template.Documents.Add(new() { Path = document.Path, Title = document.Title });
        return template;
    }

    public Task ApplyAsync(OpenProject open, ProjectTemplate template, ProjectSettings settings) => open.RunAsync(async () =>
    {
        foreach (var path in template.Folders.Select(folder => folder.Path).Concat(template.Documents.Select(document => Project.ParentPath(document.Path))).Where(path => path != ""))
        {
            var segments = path.Split('/');
            for (var depth = 1; depth <= segments.Length; depth++) open.Files.CreateFolder(string.Join('/', segments[..depth]));
        }
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var document in template.Documents)
        {
            if (open.Files.Exists(document.Path)) continue;
            var id = Guid.NewGuid().ToString();
            await open.Files.WriteAsync(document.Path, Encode($"---\nwriter_id: {id}\n---\n\n", document.Content ?? ""), overwrite: false);
            ids[document.Path] = id;
        }
        await open.RescanAsync();
        var candidate = open.Current.Manifest.Clone();
        candidate.Settings = settings;
        foreach (var document in template.Documents)
        {
            if (!ids.TryGetValue(document.Path, out var id) || !candidate.Documents.TryGetValue(id, out var metadata)) continue;
            metadata.Title = document.Title;
            metadata.WordGoal = IsFolderDocument(document.Path) ? 0 : settings.DefaultSceneWordGoal;
        }
        foreach (var folder in template.Folders)
        {
            var manifest = folder.Path == "" ? candidate : candidate.FolderManifests.GetValueOrDefault(folder.Path);
            if (manifest is null) continue;
            var child = (string name) => folder.Path == "" ? name : folder.Path + "/" + name;
            var byName = manifest.Folders.ToDictionary(pair => pair.Value.Path, pair => pair.Key, StringComparer.Ordinal);
            manifest.PinnedView = folder.PinnedView;
            OrderService.Set(manifest, folder.ItemOrder.Select(key =>
                key.StartsWith("folder:") ? byName.GetValueOrDefault(key["folder:".Length..])
                : key.StartsWith("document:") ? ids.GetValueOrDefault(child(key["document:".Length..])) : null).OfType<string>().Distinct().ToArray());
            manifest.GridFolder = folder.GridFolder is null ? null : folder.GridFolder == "" ? candidate.Id
                : candidate.FolderManifests.GetValueOrDefault(folder.GridFolder)?.Id;
        }
        await open.CommitAsync(candidate, open.Current.Revision);
        open.Versions.Save(null);
        return true;
    });
}
