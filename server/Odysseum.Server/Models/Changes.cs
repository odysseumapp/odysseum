using ProjectSettings = Odysseum.Server.Settings.ProjectSettings;

namespace Odysseum.Server.Models;

/// <summary>What one save changes. The items are placed copies taken from the edited project, so storage reads their
/// parent, name and children and derives their paths. An item whose ID storage does not know is a creation; one whose
/// path differs from the stored path is a move or rename; a document with a body is a body write.</summary>
public sealed record Changes(ProjectSettings? Settings, IReadOnlyList<Folder> Folders, IReadOnlyList<Document> Documents, IReadOnlyList<string> RemovedFolders)
{
    public static readonly Changes None = new(null, [], [], []);

    public bool IsEmpty => Settings is null && Folders.Count == 0 && Documents.Count == 0 && RemovedFolders.Count == 0;
}
