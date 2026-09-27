using Odysseum.Abstractions.Documents;
using Odysseum.Abstractions.Exceptions;
using Odysseum.Abstractions.Projects;
using Odysseum.Server.API.Models;
using Odysseum.Server.Models;

namespace Odysseum.Server.API.Views;

/// <summary>Builds the API response records from a project.</summary>
public sealed class ProjectViews(IDocumentService documents)
{
    public ProjectResponse View(Project project) => new(project.Id, project.Settings.Clone(), project.Revision,
        project.Walk().Select((document, index) => Summary(document, index)).ToArray(), project.Warning,
        project.Folders.Select(Folder).ToArray());

    /// <summary>The document with its prose. The document must have its body loaded.</summary>
    public DocumentContent View(Project project, Document document)
    {
        var body = document.Body ?? throw new InvalidOperationException("The document body is not loaded.");
        var position = project.Walk().Select((d, index) => (d, index)).FirstOrDefault(pair => pair.d.Id == document.Id).index;
        return new(Summary(document, position), body);
    }

    /// <summary>The document with its prose, loading the body when it is not loaded yet.</summary>
    public async Task<DocumentContent> ViewAsync(Project project, IDocument document)
    {
        var model = document.Body is null ? (Document)await documents.OpenAsync(project.Branch, document.Id) : (Document)document;
        return View(project, model);
    }

    public async Task<IReadOnlyList<DocumentContent>> DocumentsAsync(Project project) =>
        (await documents.OpenAllAsync(project.Branch)).Select((document, index) => new DocumentContent(Summary((Document)document, index), document.Body!)).ToArray();

    public async Task<string> ExportAsync(Project project)
    {
        var scenes = (await documents.OpenAllAsync(project.Branch)).Cast<Document>().Where(d => d.Kind == DocumentKind.Scene && !d.IsFolderDocument);
        return $"# {project.Title}\n\n" + string.Join("\n\n---\n\n", scenes.Select(d => $"## {d.Title}\n\n{d.Body!.Trim()}")) + "\n";
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(Project project, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var results = new List<SearchResult>();
        var ordered = await documents.OpenAllAsync(project.Branch);
        for (var index = 0; index < ordered.Count; index++)
        {
            var document = (Document)ordered[index];
            var body = document.Body!;
            var position = body.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (position < 0 && !document.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !document.Synopsis.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !document.Notes.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            var start = Math.Max(0, position - 55);
            results.Add(new(Summary(document, index), body.Substring(start, Math.Min(180, body.Length - start)).Replace('\n', ' ')));
            if (results.Count == 50) break;
        }
        return results;
    }

    public static FolderSummary Folder(Folder folder) => new(folder.Id, folder.Path, folder.Name, folder.IsRoot ? null : Item.ParentPath(folder.Path),
        folder.PinnedView, folder.Children.Select(child => child.Id).ToArray(), folder.Views);

    public static DocumentSummary Summary(Document document, int order) => new(document.Id, document.Path, document.Title,
        Item.ParentPath(document.Path), document.Synopsis, document.Notes, (Enums.DocumentStatus)document.Status, document.WordGoal, order,
        document.WordCount, document.Revision, document.Modified.UtcDateTime, (Enums.DocumentKind)document.Kind, document.Links, document.LinkNotes);

    public static Project Model(IProject project) => project as Project
        ?? throw new WorkspaceException(WorkspaceError.Invalid, "That project is not open in this workspace.");
}
