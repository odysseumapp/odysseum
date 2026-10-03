using System.Text;
using System.Text.Json;
using Odysseum.Abstractions.Changes;
using Odysseum.Abstractions.Documents;
using Odysseum.Abstractions.Exceptions;
using Odysseum.Abstractions.Folders;
using Odysseum.Abstractions.Items;
using Odysseum.Abstractions.Links;
using Odysseum.Abstractions.Projects;
using Odysseum.Server.Models;
using Odysseum.Server.Repositories;
using Odysseum.Server.Repositories.Files;
using Odysseum.Server.Services.Projects;
using Odysseum.Server.Services.Views;
using Odysseum.Server.Settings;
using static Odysseum.Server.Services.Documents.DocumentRules;
using ProjectSettings = Odysseum.Abstractions.Projects.ProjectSettings;
using ValidatedSettings = Odysseum.Server.Settings.ProjectSettings;

namespace Odysseum.Server.Services;

/// <summary>The rules for projects, folders, documents and links. It reads from and writes through the workspace
/// repository, and passes on the repository's changes.</summary>
public sealed class ProjectService : IProjectService
{
    private const int MaxSearchResults = 50;
    private const int MaxLinksPerDocument = 200;
    private const int MaxNoteLength = 2000;
    private readonly IWorkspaceRepository _workspace;
    private readonly IProjectLock _projectLock;
    private readonly ITemplateRepository _templateRepository;
    private readonly ISettingsProvider? _settings;
    private readonly ViewCatalog _views;
    private readonly ProjectTemplates _templates;

    public ProjectService(IWorkspaceRepository workspace, IProjectLock projectLock, ITemplateRepository templateRepository,
        ISettingsProvider? settings = null, ViewCatalog? views = null)
    {
        _workspace = workspace;
        _projectLock = projectLock;
        _templateRepository = templateRepository;
        _settings = settings;
        _views = views ?? new ViewCatalog([]);
        _templates = new ProjectTemplates(this, workspace, _views);
        workspace.Changed += (_, e) => Changed?.Invoke(this, e);
    }

    public event EventHandler<ChangesEventArgs>? Changed;

    // Reads

    public async Task<T> GetAsync<T>(string id) where T : IItem
    {
        object item = typeof(T) == typeof(IProject) ? await FindProjectAsync(id)
            : typeof(T) == typeof(IFolder) ? await FindFolderAsync(id)
            : typeof(T) == typeof(IDocument) ? await FindDocumentAsync(id)
            : typeof(T) == typeof(ILink) ? await FindLinkAsync(id)
            : throw new NotSupportedException($"There are no {typeof(T).Name} items. Use IProject, IFolder, IDocument or ILink.");
        return (T)item;
    }

    public async Task<IReadOnlyList<T>> GetAllAsync<T>(string projectId) where T : IProjectItem
    {
        object items = typeof(T) == typeof(IFolder) ? await _workspace.GetAllAsync<Folder>(projectId)
            : typeof(T) == typeof(IDocument) ? await _workspace.GetAllAsync<Document>(projectId)
            : typeof(T) == typeof(ILink) ? await _workspace.GetAllAsync<Link>(projectId)
            : throw new NotSupportedException($"A project has no {typeof(T).Name} items. Use IFolder, IDocument or ILink.");
        return (IReadOnlyList<T>)items;
    }

    public async Task<IReadOnlyList<IProject>> GetProjectsAsync()
    {
        await _workspace.FindNewProjectsAsync();
        return (await _workspace.GetProjectsAsync())
            .OrderBy(project => project.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(project => project.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<IReadOnlyList<IDocument>> GetDocumentsInOrderAsync(string projectId) => await DocumentsInOrderAsync(projectId);

    public async Task<IReadOnlyList<IDocument>> GetChildrenAsync(string folderId)
    {
        var folder = await FindFolderAsync(folderId);
        return TreeOrder.Children(folder, await _workspace.GetAllAsync<Document>(folder.ProjectId));
    }

    public async Task<IReadOnlyList<ILink>> GetLinksForDocumentAsync(string documentId)
    {
        await FindDocumentAsync(documentId);
        return await _workspace.GetLinksForDocumentAsync(documentId);
    }

    public Task<string> GetDocumentTextAsync(string documentId) => _workspace.ReadTextAsync(documentId);

    public async Task<IReadOnlyList<DocumentSearchResult>> SearchDocumentsAsync(string projectId, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var results = new List<DocumentSearchResult>();
        foreach (var document in await DocumentsInOrderAsync(projectId))
        {
            var body = await _workspace.ReadTextAsync(document.Id);
            var position = body.IndexOf(text, StringComparison.OrdinalIgnoreCase);
            if (position < 0 && !document.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
                && !document.Synopsis.Contains(text, StringComparison.OrdinalIgnoreCase)
                && !document.Notes.Contains(text, StringComparison.OrdinalIgnoreCase)) continue;
            var start = Math.Max(0, position - 55);
            results.Add(new DocumentSearchResult(document, body.Substring(start, Math.Min(180, body.Length - start)).Replace('\n', ' ')));
            if (results.Count == MaxSearchResults) break;
        }
        return results;
    }

    public async Task<string> ExportMarkdownAsync(string projectId)
    {
        var project = await FindProjectAsync(projectId);
        var scenes = new List<string>();
        foreach (var document in await DocumentsInOrderAsync(projectId))
        {
            if (document.Kind != DocumentKind.Scene || document.IsFolderDocument) continue;
            scenes.Add($"## {document.Title}\n\n{(await _workspace.ReadTextAsync(document.Id)).Trim()}");
        }
        return new StringBuilder($"# {project.Title}\n\n").Append(string.Join("\n\n---\n\n", scenes)).Append('\n').ToString();
    }

    // Projects

    public async Task<IProject> CreateProjectAsync(string title, int? wordGoal = null, string? templateName = null)
    {
        title = ValidateTitle(title);
        var template = FindTemplate(templateName);
        var settings = Validate(new ProjectSettings(title, wordGoal ?? template.Settings.WordGoal, template.Settings.DefaultSceneWordGoal));
        var project = await _workspace.AddProjectAsync(settings.Title, settings.WordGoal, settings.DefaultSceneWordGoal);
        await _projectLock.RunLockedAsync(project.Id, () => _templates.ApplyAsync(project, template));
        return await FindProjectAsync(project.Id);
    }

    public async Task<IProject> UpdateProjectSettingsAsync(string projectId, ProjectSettings settings, string expectedETag)
    {
        var project = await FindProjectAsync(projectId);
        var validated = Validate(settings);
        return await _workspace.UpdateProjectAsync(project with
        {
            Title = validated.Title, WordGoal = validated.WordGoal, DefaultSceneWordGoal = validated.DefaultSceneWordGoal,
        }, expectedETag);
    }

    public async Task SaveAsTemplateAsync(string projectId, string name)
    {
        name = TemplateRepository.ValidName(name);
        _templateRepository.Save(await _templates.CaptureAsync(await FindProjectAsync(projectId), name));
    }

    // Folders

    public async Task<IFolder> CreateFolderAsync(string parentFolderId, string name)
    {
        name = name?.Trim() ?? "";
        if (name.Length == 0 || name.Contains('/') || !FileManager.IsSafePath(name))
            throw new WorkspaceException(WorkspaceError.Invalid, "That folder name is not allowed.");
        var parent = await FindFolderAsync(parentFolderId);
        return await _workspace.AddFolderAsync(new Folder(Guid.NewGuid().ToString(), parent.ProjectId, name, ProjectPaths.Join(parent.Path, name), parent.Id,
            [], null, null, new Dictionary<string, JsonElement>(StringComparer.Ordinal), ""));
    }

    /// <summary>Sets the pinned view and changes the settings of the views named in the layout. A view not named keeps
    /// its settings; a JSON null removes a view's settings.</summary>
    public async Task<IFolder> UpdateFolderLayoutAsync(string folderId, FolderLayout layout, string expectedETag)
    {
        var folder = await FindFolderAsync(folderId);
        if (layout.PinnedView is not null) ViewNames.Check(layout.PinnedView);
        var projectFolders = (await _workspace.GetAllAsync<Folder>(folder.ProjectId)).Select(other => other.Id).ToHashSet(StringComparer.Ordinal);
        _views.CheckFolders(projectFolders.Contains, layout.Views);
        var views = new Dictionary<string, JsonElement>(folder.Views, StringComparer.Ordinal);
        foreach (var (name, settings) in layout.Views ?? new Dictionary<string, JsonElement>())
        {
            ViewNames.Check(name);
            if (ViewNames.Removes(settings)) views.Remove(name);
            else views[name] = settings.Clone();
        }
        ViewNames.CheckSettings(views);
        return await _workspace.UpdateFolderAsync(folder with { PinnedView = layout.PinnedView, Views = views }, expectedETag);
    }

    public async Task<FolderMoveResult> MoveFolderToFolderAsync(string folderId, string targetFolderId, int index, string expectedETag)
    {
        var folder = await FindFolderAsync(folderId);
        if (folder.IsRoot) throw new WorkspaceException(WorkspaceError.Invalid, "The project folder itself cannot be moved.");
        var target = await FindFolderAsync(targetFolderId);
        if (target.ProjectId != folder.ProjectId) throw new WorkspaceException(WorkspaceError.Invalid, "A folder can only move inside its own project.");
        var moved = await _workspace.MoveFolderAsync(folderId, targetFolderId, index, expectedETag);
        return new FolderMoveResult(moved, await FindFolderAsync(folder.ParentFolderId!), await FindFolderAsync(targetFolderId));
    }

    /// <summary>Deletes the folder with its own document. View settings in other folders that name it are cleared.</summary>
    public async Task DeleteFolderAsync(string folderId, string expectedETag)
    {
        var folder = await FindFolderAsync(folderId);
        if (folder.IsRoot) throw new WorkspaceException(WorkspaceError.Invalid, "The project folder itself cannot be deleted.");
        if (folder.ParentFolderId == folder.ProjectId && DefaultFolders.IsDefaultFolder(folder.Name)
            && !(_settings?.GetSettings().AllowDeletingDefaultFolders ?? false))
            throw new WorkspaceException(WorkspaceError.Forbidden, "Default project folders stay unless the server setting 'Allow deleting default project folders' is on.");
        await _projectLock.RunLockedAsync(folder.ProjectId, async () =>
        {
            await _workspace.DeleteFolderAsync(folderId, expectedETag);
            foreach (var other in await _workspace.GetAllAsync<Folder>(folder.ProjectId))
            {
                var cleared = _views.WithoutFolder(other.Views, folderId);
                if (cleared.Count == 0) continue;
                var views = new Dictionary<string, JsonElement>(other.Views, StringComparer.Ordinal);
                foreach (var (name, settings) in cleared)
                {
                    if (ViewNames.Removes(settings)) views.Remove(name);
                    else views[name] = settings;
                }
                await _workspace.UpdateFolderAsync(other with { Views = views }, other.ETag);
            }
        });
    }

    // Documents

    public async Task<IDocument> CreateDocumentAsync(string folderId, string title, string? text = null)
    {
        title = ValidateTitle(title);
        var folder = await FindFolderAsync(folderId);
        var project = await FindProjectAsync(folder.ProjectId);
        return await _projectLock.RunLockedAsync(folder.ProjectId, async () =>
        {
            var name = await FreeFileNameAsync(folder, FileName(title), ".md");
            var path = ProjectPaths.Join(folder.Path, name);
            var document = new Document(Guid.NewGuid().ToString(), folder.ProjectId, folder.Id, name, path, KindOf(path), false, title, "", "",
                DocumentStatus.Draft, project.DefaultSceneWordGoal, 0, default, "");
            return (IDocument)await _workspace.AddDocumentAsync(document, string.IsNullOrEmpty(text) ? StarterContent(path) : text);
        });
    }

    public async Task<IDocument> UpdateDocumentTextAsync(string documentId, string text, string expectedETag)
    {
        if (text is null) throw new WorkspaceException(WorkspaceError.Invalid, "Document text is required.");
        return await _workspace.WriteTextAsync(documentId, text, expectedETag);
    }

    public async Task<IDocument> UpdateDocumentDetailsAsync(string documentId, DocumentDetails details, string expectedETag)
    {
        var document = await FindDocumentAsync(documentId);
        if (details.Synopsis is null || details.Notes is null) throw new WorkspaceException(WorkspaceError.Invalid, "Synopsis and notes are required.");
        if (!Enum.IsDefined(details.Status)) throw new WorkspaceException(WorkspaceError.Invalid, "Unknown document status.");
        if (details.WordGoal is < 0 or > 10000000) throw new WorkspaceException(WorkspaceError.Invalid, "Invalid word goal.");
        if (details.Synopsis.Length > 20000 || details.Notes.Length > 100000) throw new WorkspaceException(WorkspaceError.Invalid, "The synopsis or the notes are too long.");
        return await _workspace.UpdateDocumentAsync(document with
        {
            Title = ValidateTitle(details.Title), Synopsis = details.Synopsis, Notes = details.Notes, Status = details.Status, WordGoal = details.WordGoal,
        }, expectedETag);
    }

    public async Task<IDocument> RenameDocumentAsync(string documentId, string name, string expectedETag)
    {
        var document = await FindDocumentAsync(documentId);
        if (document.IsFolderDocument) throw new WorkspaceException(WorkspaceError.Invalid, "A folder's own document keeps its name.");
        return await _workspace.UpdateDocumentAsync(document with { Name = FileName(ValidateTitle(name)) + Path.GetExtension(document.Name) }, expectedETag);
    }

    public async Task<DocumentMoveResult> MoveDocumentToFolderAsync(string documentId, string targetFolderId, int index, string expectedETag)
    {
        var document = await FindDocumentAsync(documentId);
        if (document.IsFolderDocument) throw new WorkspaceException(WorkspaceError.Invalid, "A folder's own document stays with its folder.");
        var target = await FindFolderAsync(targetFolderId);
        if (target.ProjectId != document.ProjectId) throw new WorkspaceException(WorkspaceError.Invalid, "A document can only move inside its own project.");
        var moved = await _workspace.MoveDocumentAsync(documentId, targetFolderId, index, expectedETag);
        return new DocumentMoveResult(moved, await FindFolderAsync(document.FolderId), await FindFolderAsync(targetFolderId));
    }

    // Links

    public async Task<ILink> CreateLinkAsync(string firstDocumentId, string secondDocumentId, string note = "")
    {
        var first = await FindDocumentAsync(firstDocumentId);
        var second = await FindDocumentAsync(secondDocumentId);
        if (first.Id == second.Id) throw new WorkspaceException(WorkspaceError.Invalid, "A document cannot be linked to itself.");
        if (first.ProjectId != second.ProjectId) throw new WorkspaceException(WorkspaceError.Invalid, "Only documents of the same project can be linked.");
        if ((await _workspace.GetLinksForDocumentAsync(first.Id)).Count >= MaxLinksPerDocument
            || (await _workspace.GetLinksForDocumentAsync(second.Id)).Count >= MaxLinksPerDocument)
            throw new WorkspaceException(WorkspaceError.Invalid, "A document can have at most 200 links.");
        return await _workspace.AddLinkAsync(new Link(Guid.NewGuid().ToString(), first.ProjectId, first.Id, second.Id, CheckNote(note), ""));
    }

    public async Task<ILink> UpdateLinkNoteAsync(string linkId, string note, string expectedETag)
    {
        var link = await FindLinkAsync(linkId);
        return await _workspace.UpdateLinkAsync(link with { Note = CheckNote(note) }, expectedETag);
    }

    public Task DeleteLinkAsync(string linkId, string expectedETag) => _workspace.DeleteLinkAsync(linkId, expectedETag);

    // Helpers

    private async Task<IReadOnlyList<Document>> DocumentsInOrderAsync(string projectId)
    {
        var root = await FindFolderAsync((await FindProjectAsync(projectId)).RootFolderId);
        return TreeOrder.Documents(root, await _workspace.GetAllAsync<Folder>(projectId), await _workspace.GetAllAsync<Document>(projectId));
    }

    /// <summary>The file name, with a number added when another document in the folder already has it.</summary>
    private async Task<string> FreeFileNameAsync(Folder folder, string stem, string extension)
    {
        var taken = (await _workspace.GetAllAsync<Document>(folder.ProjectId)).Where(document => document.FolderId == folder.Id)
            .Select(document => document.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = stem + extension;
        for (var suffix = 2; taken.Contains(name); suffix++) name = $"{stem}-{suffix}{extension}";
        return name;
    }

    /// <summary>The template with that name, or the <c>Default</c> template when no name is given.</summary>
    private ProjectTemplate FindTemplate(string? name) =>
        _templateRepository.Get(string.IsNullOrWhiteSpace(name) ? TemplateRepository.DefaultName : name);

    private static ValidatedSettings Validate(ProjectSettings settings)
    {
        var validated = ValidatedSettings.From(new ValidatedSettings
        {
            Title = settings.Title, WordGoal = settings.WordGoal, DefaultSceneWordGoal = settings.DefaultSceneWordGoal,
        }, out var error);
        return error is null ? validated : throw new WorkspaceException(WorkspaceError.Invalid, error);
    }

    private static string CheckNote(string? note)
    {
        note = note?.Trim() ?? "";
        return note.Length <= MaxNoteLength ? note : throw new WorkspaceException(WorkspaceError.Invalid, "A link note is too long.");
    }

    private async Task<Project> FindProjectAsync(string projectId) => await _workspace.GetAsync<Project>(projectId)
        ?? throw new WorkspaceException(WorkspaceError.NotFound, "No project has that ID.");

    private async Task<Folder> FindFolderAsync(string folderId) => await _workspace.GetAsync<Folder>(folderId)
        ?? throw new WorkspaceException(WorkspaceError.NotFound, "The folder no longer exists.");

    private async Task<Document> FindDocumentAsync(string documentId) => await _workspace.GetAsync<Document>(documentId)
        ?? throw new WorkspaceException(WorkspaceError.NotFound, "This document was removed or moved outside the workspace.");

    private async Task<Link> FindLinkAsync(string linkId) => await _workspace.GetAsync<Link>(linkId)
        ?? throw new WorkspaceException(WorkspaceError.NotFound, "The link no longer exists.");
}
