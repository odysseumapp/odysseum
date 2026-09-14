using Odysseum.Server.Settings;

namespace Odysseum.Server.API.Models;

/// <summary>A project as listed in the workspace. <c>Slug</c> is the folder name used in URLs.</summary>
public record ProjectInfo(string Slug, string Title, string Id, DateTime LastModified);

public record CreateProjectRequest(string Title, int? WordGoal);

/// <summary>The open project: its settings plus every document currently on disk, in manuscript order.</summary>
public record ProjectResponse(string Id, ProjectSettings Settings, string Revision,
    IReadOnlyList<DocumentSummary> Documents, string? Warning);

/// <summary><c>Revision</c> is the project metadata revision the client last saw; stale values are rejected with 409.</summary>
public record ProjectSettingsRequest(string Title, int WordGoal, int DefaultSceneWordGoal, string Revision);

public record ReorderRequest(string[] Ids, string Revision);
