using Odysseum.Abstractions.Folders;
using Odysseum.Abstractions.Projects;
using Odysseum.Server.Models;
using Odysseum.Server.Repositories;
using Odysseum.Server.Services.Views;
using static Odysseum.Server.Services.Documents.DocumentRules;

namespace Odysseum.Server.Services;

/// <summary>Saves a project's folders, documents and layouts as a template, and makes them in a new project. It makes
/// items through the project service, so that the same rules apply.</summary>
internal sealed class ProjectTemplates(IProjectService projects, IWorkspaceRepository workspace, ViewCatalog views)
{
    public async Task<ProjectTemplate> CaptureAsync(Project project, string name)
    {
        var template = new ProjectTemplate
        {
            Name = name,
            Settings = new() { WordGoal = project.WordGoal, DefaultSceneWordGoal = project.DefaultSceneWordGoal },
        };
        var folders = await workspace.GetAllAsync<Folder>(project.Id);
        var documents = await workspace.GetAllAsync<Document>(project.Id);
        var pathById = folders.ToDictionary(folder => folder.Id, folder => folder.Path, StringComparer.Ordinal);
        var nameById = folders.Select(folder => (folder.Id, folder.Name)).Concat(documents.Select(document => (document.Id, document.Name)))
            .ToDictionary(item => item.Id, item => item.Name, StringComparer.Ordinal);
        foreach (var folder in folders.OrderBy(folder => folder.Path, StringComparer.Ordinal))
        {
            template.Folders.Add(new()
            {
                Path = folder.Path,
                PinnedView = folder.PinnedView,
                Children = [.. folder.ChildIds.Select(id => nameById.GetValueOrDefault(id)).OfType<string>()],
                Views = folder.Views.Count == 0 ? null : views.MapFolders(folder.Views, id => pathById.GetValueOrDefault(id)),
            });
        }
        template.Documents.AddRange(documents.Where(document => !document.IsFolderDocument)
            .OrderBy(document => document.Path, StringComparer.Ordinal)
            .Select(document => new TemplateDocument { Path = document.Path, Title = document.Title }));
        return template;
    }

    /// <summary>Makes the template's folders, documents, layouts and order in the project. The caller holds the project lock.</summary>
    public async Task ApplyAsync(Project project, ProjectTemplate template)
    {
        var folderIds = new Dictionary<string, string>(StringComparer.Ordinal) { [""] = project.RootFolderId };
        var neededPaths = template.Folders.Select(folder => folder.Path)
            .Concat(template.Documents.Select(document => ProjectPaths.ParentOf(document.Path)))
            .Where(path => path.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        foreach (var path in neededPaths)
        {
            var segments = path.Split('/');
            for (var depth = 1; depth <= segments.Length; depth++)
            {
                var partial = string.Join('/', segments[..depth]);
                if (folderIds.ContainsKey(partial)) continue;
                folderIds[partial] = (await projects.CreateFolderAsync(folderIds[ProjectPaths.ParentOf(partial)], segments[depth - 1])).Id;
            }
        }
        var documentIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var document in template.Documents)
        {
            if (IsFolderDocument(document.Path)) continue;
            var created = await projects.CreateDocumentAsync(folderIds[ProjectPaths.ParentOf(document.Path)], document.Title,
                document.Content ?? StarterContent(document.Path));
            var fileName = ProjectPaths.NameOf(document.Path);
            if (created.Name != fileName)
                created = await projects.RenameDocumentAsync(created.Id, Path.GetFileNameWithoutExtension(fileName), created.ETag);
            documentIds[document.Path] = created.Id;
        }
        foreach (var templateFolder in template.Folders)
        {
            if (!folderIds.TryGetValue(templateFolder.Path, out var id)) continue;
            if (templateFolder.PinnedView is not null || templateFolder.Views is { Count: > 0 })
            {
                var layout = new FolderLayout
                {
                    PinnedView = templateFolder.PinnedView,
                    Views = templateFolder.Views is null ? null : views.MapFolders(templateFolder.Views, path => folderIds.GetValueOrDefault(path)),
                };
                await projects.UpdateFolderLayoutAsync(id, layout, (await projects.GetAsync<IFolder>(id)).ETag);
            }
            if (templateFolder.Children.Count == 0 || await workspace.GetAsync<Folder>(id) is not { } folder) continue;
            var ordered = templateFolder.Children
                .Select(name => ProjectPaths.Join(templateFolder.Path, name))
                .Select(path => folderIds.GetValueOrDefault(path) ?? documentIds.GetValueOrDefault(path))
                .OfType<string>().Distinct().ToList();
            await workspace.UpdateFolderAsync(folder with { ChildIds = [.. ordered, .. folder.ChildIds.Where(child => !ordered.Contains(child))] }, folder.ETag);
        }
    }
}
