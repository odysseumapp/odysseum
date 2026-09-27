using Odysseum.Abstractions.Documents;
using Odysseum.Abstractions.Folders;
using Odysseum.Server.Models;
using Odysseum.Server.Models.Editing;
using Odysseum.Server.Repositories;
using Odysseum.Server.Services.Projects;
using Odysseum.Server.Settings;
using static Odysseum.Server.Services.Documents.DocumentRules;

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
                ItemOrder = [.. folder.Children.Select(child => child is Folder sub ? "folder:" + sub.Name : "document:" + child.Name)],
                GridFolder = folder.GridFolderId is null ? null : project.Folder(folder.GridFolderId)?.Path,
            });
        }
        foreach (var document in project.Walk().Where(d => !d.IsFolderDocument))
            template.Documents.Add(new() { Path = document.Path, Title = document.Title });
        return template;
    }

    /// <summary>Writes the template's folders, documents, layouts and settings into the project in one save.</summary>
    public Task ApplyAsync(ProjectSession session, ProjectTemplate template, ProjectSettings settings) => session.RunAsync(async () =>
    {
        var current = session.Current;
        var editor = new FolderEditor(current);
        var documents = editor.Documents;
        var folderIds = new Dictionary<string, string>(StringComparer.Ordinal) { [""] = current.Root.Id };
        foreach (var path in template.Folders.Select(folder => folder.Path).Concat(template.Documents.Select(document => Item.ParentPath(document.Path))).Where(path => path != "").Distinct())
        {
            var segments = path.Split('/');
            for (var depth = 1; depth <= segments.Length; depth++)
            {
                var partial = string.Join('/', segments[..depth]);
                if (folderIds.ContainsKey(partial)) continue;
                folderIds[partial] = current.FolderAt(partial)?.Id ?? editor.Create(folderIds[Item.ParentPath(partial)], segments[depth - 1]).Id;
            }
        }
        var documentIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var document in template.Documents)
        {
            if (current.Documents.Any(d => d.Path == document.Path)) continue;
            var created = documents.Create(folderIds[Item.ParentPath(document.Path)], document.Title, document.Content, Path.GetFileName(document.Path));
            documents.SetDetails(created.Id, new DocumentDetails { WordGoal = IsFolderDocument(document.Path) ? 0 : settings.DefaultSceneWordGoal });
            documentIds[document.Path] = created.Id;
        }
        foreach (var folder in template.Folders)
        {
            if (!folderIds.TryGetValue(folder.Path, out var id)) continue;
            var view = folder.PinnedView is not null && Enum.TryParse<FolderView>(folder.PinnedView, ignoreCase: true, out var parsed) ? parsed : (FolderView?)null;
            editor.SetLayout(id, new FolderLayout { PinnedView = view, GridFolderId = folder.GridFolder is null ? null : folderIds.GetValueOrDefault(folder.GridFolder) });
            var ordered = folder.ItemOrder.Select(key =>
                key.StartsWith("folder:") ? folderIds.GetValueOrDefault(Item.Join(folder.Path, key["folder:".Length..]))
                : key.StartsWith("document:") ? documentIds.GetValueOrDefault(Item.Join(folder.Path, key["document:".Length..])) : null)
                .OfType<string>().Distinct().ToArray();
            for (var index = 0; index < ordered.Length; index++) editor.Move(ordered[index], id, index);
        }
        editor.SetSettings(settings);
        await session.SaveAsync(editor.Changes(), current.Revision);
        return true;
    });
}
