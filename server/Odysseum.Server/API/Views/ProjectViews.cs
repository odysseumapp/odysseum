using Odysseum.Abstractions.Documents;
using Odysseum.Abstractions.Exceptions;
using Odysseum.Abstractions.Projects;
using Odysseum.Server.API.Models;
using Odysseum.Server.Models;
using Odysseum.Server.Services;
using static Odysseum.Server.Services.Documents.MarkdownDocumentCodec;

namespace Odysseum.Server.API.Views;

public sealed class ProjectViews(IOrderService order)
{
    public async Task<ProjectResponse> ProjectAsync(IProject project)
    {
        var model = Model(project);
        var documents = (await order.DocumentsAsync(model)).Select((document, index) => Summary((Document)document, index)).ToArray();
        var folders = new List<FolderSummary>();
        foreach (var folder in model.Folders) folders.Add(await FolderAsync(folder));
        return new(model.Id, model.Settings.Clone(), model.Revision, documents, model.Warning, folders);
    }

    public async Task<DocumentContent> DocumentAsync(IDocument document)
    {
        var model = (Document)document;
        var ordered = await order.DocumentsAsync(model.Project);
        var position = ordered.Select((d, index) => (d, index)).FirstOrDefault(pair => pair.d.Id == model.Id).index;
        return new(Summary(model, position), model.Body);
    }

    public async Task<IReadOnlyList<DocumentContent>> DocumentsAsync(IProject project) =>
        (await order.DocumentsAsync(Model(project))).Select((document, index) => new DocumentContent(Summary((Document)document, index), document.Body)).ToArray();

    public async Task<string> ExportAsync(IProject project)
    {
        var model = Model(project);
        var scenes = (await order.DocumentsAsync(model)).Cast<Document>().Where(d => d.Kind == DocumentKind.Scene && !d.IsFolderDocument);
        return $"# {model.Title}\n\n" + string.Join("\n\n---\n\n", scenes.Select(d => $"## {d.Title}\n\n{d.Body.Trim()}")) + "\n";
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(IProject project, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var results = new List<SearchResult>();
        var ordered = await order.DocumentsAsync(Model(project));
        for (var index = 0; index < ordered.Count; index++)
        {
            var document = (Document)ordered[index];
            var position = document.Body.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (position < 0 && !document.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !document.Synopsis.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !document.Notes.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            var start = Math.Max(0, position - 55);
            results.Add(new(Summary(document, index), document.Body.Substring(start, Math.Min(180, document.Body.Length - start)).Replace('\n', ' ')));
            if (results.Count == 50) break;
        }
        return results;
    }

    public async Task<FolderSummary> FolderAsync(Folder folder)
    {
        var keys = (await order.ChildrenAsync(folder)).Select(child => child.Folder is { } sub ? "folder:" + sub.Name : child.Document!.Id).ToArray();
        return new(folder.Id, folder.Path, folder.Name, folder.IsRoot ? null : Project.ParentPath(folder.Path),
            folder.PinnedView?.ToString().ToLowerInvariant(), keys, folder.GridFolderId);
    }

    public static DocumentSummary Summary(Document document, int order) => new(document.Id, document.Path, document.Title,
        Project.ParentPath(document.Path), document.Synopsis, document.Notes, (Enums.DocumentStatus)document.Status, document.WordGoal, order,
        CountWords(document.Body), document.Revision, document.Modified.UtcDateTime, (Enums.DocumentKind)document.Kind, document.LinkIds, document.LinkNotes);

    public static Project Model(IProject project) => project as Project
        ?? throw new WorkspaceException(WorkspaceError.Invalid, "That project is not open in this workspace.");
}
