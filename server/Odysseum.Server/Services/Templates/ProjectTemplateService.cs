using Odysseum.Abstractions.Documents;
using Odysseum.Abstractions.Exceptions;
using Odysseum.Abstractions.Folders;
using Odysseum.Server.Models;
using Odysseum.Server.Repositories;
using Odysseum.Server.Services.Views;
using static Odysseum.Server.Services.Documents.DocumentRules;

namespace Odysseum.Server.Services.Templates;

/// <summary>Saves a project's folders, documents and layouts as a template, and makes them in a new project.</summary>
public sealed class ProjectTemplateService(IFolderService folderService, IDocumentService documentService, IFolderRepository folders,
    IDocumentRepository documents, IFolderPlaceRepository folderPlaces, IDocumentPlaceRepository documentPlaces, IProjectRepository projects,
    ViewCatalog? views = null)
{
    private readonly ViewCatalog _views = views ?? ViewCatalog.Default;

    public async Task<ProjectTemplate> CaptureTemplateAsync(string projectId, string name)
    {
        var project = await projects.GetByIdAsync(projectId) ?? throw new WorkspaceException(WorkspaceError.NotFound, "No project has that ID.");
        var template = new ProjectTemplate
        {
            Name = name,
            Settings = new() { WordGoal = project.WordGoal, DefaultSceneWordGoal = project.DefaultSceneWordGoal },
        };
        var projectFolders = await folders.GetFoldersByProjectIdAsync(projectId);
        var pathById = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var folder in projectFolders) pathById[folder.Id] = await folderPlaces.GetPathByFolderIdAsync(folder.Id);
        foreach (var folder in projectFolders.OrderBy(folder => pathById[folder.Id], StringComparer.Ordinal))
        {
            var children = new List<string>();
            foreach (var id in folder.ChildIds)
            {
                if (await documents.GetByIdAsync(id) is { } document) children.Add(document.Name);
                else if (await folders.GetByIdAsync(id) is { } child) children.Add(child.Name);
            }
            template.Folders.Add(new()
            {
                Path = pathById[folder.Id],
                PinnedView = folder.PinnedView,
                Children = children,
                Views = folder.Views.Count == 0 ? null : _views.MapFolders(folder.Views, id => pathById.GetValueOrDefault(id)),
            });
        }
        foreach (var document in await documents.GetDocumentsByProjectIdAsync(projectId))
        {
            if (document.IsFolderDocument) continue;
            template.Documents.Add(new() { Path = await documentPlaces.GetPathByDocumentIdAsync(document.Id), Title = document.Title });
        }
        template.Documents.Sort((first, second) => StringComparer.Ordinal.Compare(first.Path, second.Path));
        return template;
    }

    /// <summary>Makes the template's folders, documents, layouts and order in the project, through the folder and
    /// document services. The caller holds the project lock.</summary>
    public async Task ApplyTemplateAsync(Project project, ProjectTemplate template)
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
                folderIds[partial] = (await folderService.CreateFolderAsync(folderIds[ProjectPaths.ParentOf(partial)], segments[depth - 1])).Id;
            }
        }
        var documentIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var document in template.Documents)
        {
            if (IsFolderDocument(document.Path)) continue;
            var created = await documentService.CreateDocumentAsync(folderIds[ProjectPaths.ParentOf(document.Path)], document.Title,
                document.Content ?? StarterContent(document.Path));
            var fileName = ProjectPaths.NameOf(document.Path);
            if (created.Name != fileName)
                created = await documentService.RenameDocumentAsync(created.Id, Path.GetFileNameWithoutExtension(fileName), created.ETag);
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
                    Views = templateFolder.Views is null ? null : _views.MapFolders(templateFolder.Views, path => folderIds.GetValueOrDefault(path)),
                };
                await folderService.UpdateFolderLayoutAsync(id, layout, (await folderService.GetFolderByIdAsync(id)).ETag);
            }
            var folder = await folders.GetByIdAsync(id);
            if (folder is null || templateFolder.Children.Count == 0) continue;
            var ordered = templateFolder.Children
                .Select(name => ProjectPaths.Join(templateFolder.Path, name))
                .Select(path => folderIds.GetValueOrDefault(path) ?? documentIds.GetValueOrDefault(path))
                .OfType<string>().Distinct().ToList();
            await folders.UpdateAsync(folder with { ChildIds = [.. ordered, .. folder.ChildIds.Where(child => !ordered.Contains(child))] }, folder.ETag);
        }
    }
}
