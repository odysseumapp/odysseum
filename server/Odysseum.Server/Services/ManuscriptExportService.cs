using System.Text;
using Odysseum.Abstractions.Documents;
using Odysseum.Abstractions.Projects;

namespace Odysseum.Server.Services;

/// <summary>Makes one Markdown file from the scenes of a project, in manuscript order.</summary>
public sealed class ManuscriptExportService(IProjectService projects, IDocumentService documents)
{
    public async Task<string> ExportManuscriptAsync(string projectId)
    {
        var project = await projects.GetProjectByIdAsync(projectId);
        var scenes = new List<string>();
        foreach (var document in await documents.GetDocumentsByProjectIdAsync(projectId))
        {
            if (document.Kind != DocumentKind.Scene || document.IsFolderDocument) continue;
            var text = await documents.GetDocumentTextByIdAsync(document.Id);
            scenes.Add($"## {document.Title}\n\n{text.Trim()}");
        }
        return new StringBuilder($"# {project.Title}\n\n").Append(string.Join("\n\n---\n\n", scenes)).Append('\n').ToString();
    }
}
