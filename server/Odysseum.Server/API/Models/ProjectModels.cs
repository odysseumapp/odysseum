using Odysseum.Server.Settings;

namespace Odysseum.Server.API.Models;

/// <summary>A project as listed in the workspace. <c>Slug</c> is the folder name used in URLs.</summary>
public record ProjectInfo(string Slug, string Title, string Id, DateTime LastModified);

public record CreateProjectRequest(string Title, int? WordGoal);

/// <summary>The open project: settings, documents, and all physical folders with their view layouts.</summary>
public record ProjectResponse(string Id, ProjectSettings Settings, string Revision,
    IReadOnlyList<DocumentSummary> Documents, string? Warning, IReadOnlyList<FolderSummary> Folders);

/// <summary>A physical folder, including the project root (empty path). <c>ItemOrder</c> keys are document IDs or folder:name.
/// The grid: <c>Rows</c> are document IDs; <c>Columns</c> are document IDs, folder IDs, or <c>folderId/*</c> for every document
/// under that folder in order (empty means this folder's children); <c>Axis</c> is "rows" (default) or "columns".</summary>
public record FolderSummary(string Id, string Path, string Name, string? Parent, string? PinnedView,
    string[] ItemOrder, string[] Rows, string[] Columns, string? Axis);
public record CreateFolderRequest(string Path, string Revision);
public record RemoveFolderRequest(string Path, string Revision);
public record FolderLayoutRequest(string Path, string? PinnedView, string[] ItemOrder,
    string[] Rows, string[] Columns, string? Axis, string Revision);

/// <summary><c>Revision</c> is the project metadata revision the client last saw; stale values are rejected with 409.</summary>
public record ProjectSettingsRequest(string Title, int WordGoal, int DefaultSceneWordGoal, string Revision);

public record ReorderRequest(string[] Ids, string Revision);
