using Odysseum.Server.Models;

namespace Odysseum.Server.Repositories;

/// <summary>What one write or one read of a project changed.</summary>
public sealed class StorageChanges(string projectId)
{
    public string ProjectId { get; } = projectId;
    /// <summary>True when the lists hold every item of the project. An item of the project that is not in the lists was removed.</summary>
    public bool ReplacesProject { get; init; }
    public List<Project> Projects { get; } = [];
    public List<Folder> Folders { get; } = [];
    public List<Document> Documents { get; } = [];
    public List<Link> Links { get; } = [];
    public List<string> RemovedProjectIds { get; } = [];
    public List<string> RemovedFolderIds { get; } = [];
    public List<string> RemovedDocumentIds { get; } = [];
    public List<string> RemovedLinkIds { get; } = [];
    /// <summary>The folders and documents that a move put in another folder. Only a move write fills it.</summary>
    public HashSet<string> MovedIds { get; } = new(StringComparer.Ordinal);
}
