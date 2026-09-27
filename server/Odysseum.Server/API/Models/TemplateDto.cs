using Odysseum.Server.Repositories;

namespace Odysseum.Server.API.Models;

/// <summary>A project template: what a project made from it will hold, without the document text.</summary>
public record TemplateDto(string Name, int WordGoal, int DefaultSceneWordGoal, IReadOnlyList<TemplateFolderDto> Folders,
    IReadOnlyList<TemplateDocumentDto> Documents)
{
    public static TemplateDto FromTemplate(ProjectTemplate template) => new(template.Name, template.Settings.WordGoal,
        template.Settings.DefaultSceneWordGoal,
        [.. template.Folders.Select(folder => new TemplateFolderDto(folder.Path, folder.PinnedView, folder.Children, folder.Views))],
        [.. template.Documents.Select(document => new TemplateDocumentDto(document.Path, document.Title))]);
}
