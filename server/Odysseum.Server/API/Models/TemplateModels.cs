namespace Odysseum.Server.API.Models;

/// <summary>Saves the named project's current goals, folders, and documents as a template.</summary>
public record SaveTemplateRequest(string Project);

/// <summary>A project template as the interface needs it: what a project made from it will hold, without the prose.</summary>
public record TemplateResponse(string Name, TemplateSettingsResponse Settings,
    IReadOnlyList<TemplateFolderResponse> Folders, IReadOnlyList<TemplateDocumentResponse> Documents);
public record TemplateSettingsResponse(int WordGoal, int DefaultSceneWordGoal);
/// <summary>The project root is the empty path. <c>ItemOrder</c> keys are <c>folder:Name</c> or <c>document:File.md</c>.</summary>
public record TemplateFolderResponse(string Path, string? PinnedView, IReadOnlyList<string> ItemOrder, string? GridFolder);
public record TemplateDocumentResponse(string Path, string Title);
