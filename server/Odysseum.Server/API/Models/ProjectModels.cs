using Odysseum.Server.Settings;

namespace Odysseum.Server.API.Models;

/// <summary>A project as listed in the workspace. <c>Slug</c> is the folder name used in URLs.</summary>
public record ProjectInfo(string Slug, string Title, string Id, DateTime LastModified);

/// <summary><c>Template</c> names the project template to start from. Without it, the project starts from <c>Default</c>.</summary>
public record CreateProjectRequest(string Title, int? WordGoal, string? Template = null);

/// <summary>The open project: settings, documents, and all physical folders with their view layouts.</summary>
public record ProjectResponse(string Id, ProjectSettings Settings, string Revision,
    IReadOnlyList<DocumentSummary> Documents, string? Warning, IReadOnlyList<FolderSummary> Folders);

/// <summary>A physical folder. The project root is the one with the empty path. <c>ItemOrder</c> keys are document IDs or folder:name.
/// <c>GridFolder</c> is the ID of the folder whose documents are the columns of this folder's grid. Null means the default.</summary>
public record FolderSummary(string Id, string Path, string Name, string? Parent, string? PinnedView,
    string[] ItemOrder, string? GridFolder);
public record CreateFolderRequest(string Path, string Revision);
public record RemoveFolderRequest(string Path, string Revision);
public record FolderLayoutRequest(string Path, string? PinnedView, string[] ItemOrder, string? GridFolder, string Revision);

/// <summary><c>Revision</c> is the project metadata revision the client last saw. The server rejects a stale one with 409.</summary>
public record ProjectSettingsRequest(string Title, int WordGoal, int DefaultSceneWordGoal, string Revision);

public record ReorderRequest(string[] Ids, string Revision);
