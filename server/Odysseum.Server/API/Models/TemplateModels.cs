using System.Text.Json;

namespace Odysseum.Server.API.Models;

/// <summary>Saves the named project's current goals, folders, and documents as a template.</summary>
public record SaveTemplateRequest(string Project);

/// <summary>A project template as the interface needs it: what a project made from it will hold, without the prose.</summary>
public record TemplateResponse(string Name, TemplateSettingsResponse Settings,
    IReadOnlyList<TemplateFolderResponse> Folders, IReadOnlyList<TemplateDocumentResponse> Documents);
public record TemplateSettingsResponse(int WordGoal, int DefaultSceneWordGoal);
/// <summary>The project root is the empty path. <c>Children</c> are the names of the subfolders and documents in order.
/// In <c>Views</c>, a setting that names a folder holds its path.</summary>
public record TemplateFolderResponse(string Path, string? PinnedView, IReadOnlyList<string> Children, IReadOnlyDictionary<string, JsonElement>? Views);
public record TemplateDocumentResponse(string Path, string Title);
