using System.Text.Json;
using Odysseum.Server.Settings;

namespace Odysseum.Server.API.Models;

/// <summary><c>Template</c> names the project template to start from. Without it, the project starts from <c>Default</c>.</summary>
public record CreateProjectRequest(string Title, int? WordGoal, string? Template = null);

/// <summary>The open project: settings, documents, and all physical folders with their view layouts.</summary>
public record ProjectResponse(string Id, ProjectSettings Settings, string Revision,
    IReadOnlyList<DocumentSummary> Documents, string? Warning, IReadOnlyList<FolderSummary> Folders);

/// <summary>A physical folder. The project root is the one with the empty path. <c>Children</c> lists the IDs of its
/// subfolders and documents in order. <c>PinnedView</c> is the name of the view the folder opens in. <c>Views</c> holds
/// settings by view name; the server stores them without reading them.</summary>
public record FolderSummary(string Id, string Path, string Name, string? Parent, string? PinnedView,
    string[] Children, IReadOnlyDictionary<string, JsonElement> Views);
public record CreateFolderRequest(string Path, string Revision);
public record RemoveFolderRequest(string Path, string Revision);
/// <summary><c>PinnedView</c> replaces the pinned view; null unpins it. Each view in <c>Views</c> gets the settings given
/// (a JSON object), a null removes that view's settings, and views not given keep theirs.</summary>
public record FolderLayoutRequest(string Path, string? PinnedView, Dictionary<string, JsonElement>? Views, string Revision);

/// <summary>Puts the folder or document <c>Id</c> at <c>Index</c> among the children of the folder <c>TargetFolder</c>
/// (a folder ID). This is how the order changes.</summary>
public record MoveItemRequest(string Id, string TargetFolder, int Index, string Revision);

/// <summary><c>Revision</c> is the project metadata revision the client last saw. The server rejects a stale one with 409.</summary>
public record ProjectSettingsRequest(string Title, int WordGoal, int DefaultSceneWordGoal, string Revision);
