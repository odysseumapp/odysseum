using Odysseum.Server.Models;

namespace Odysseum.Server.Repositories;

/// <summary>What one write or one read of a project changed. The storage sends it to every repository, and each
/// repository takes the items of its own type.</summary>
public sealed class StorageChanges(string projectId)
{
    public string ProjectId { get; } = projectId;
    /// <summary>True when the lists hold every item of the project. An item of the project that is not in the lists was removed.</summary>
    public bool ReplacesProject { get; init; }
    public List<Project> Projects { get; } = [];
    public List<Folder> Folders { get; } = [];
    public List<Document> Documents { get; } = [];
    public List<Link> Links { get; } = [];
    public List<FolderPlace> FolderPlaces { get; } = [];
    public List<DocumentPlace> DocumentPlaces { get; } = [];
    public List<string> RemovedProjectIds { get; } = [];
    public List<string> RemovedFolderIds { get; } = [];
    public List<string> RemovedDocumentIds { get; } = [];
    public List<string> RemovedFolderPlaceIds { get; } = [];
    public List<string> RemovedDocumentPlaceIds { get; } = [];
    public List<string> RemovedLinkIds { get; } = [];
}
