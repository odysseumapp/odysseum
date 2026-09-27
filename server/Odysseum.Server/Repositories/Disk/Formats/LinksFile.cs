namespace Odysseum.Server.Repositories.Disk.Formats;

/// <summary><c>&lt;project&gt;/.odysseum/links.json</c>: every link between two documents of the project, each once.
/// A link to a document whose file is gone stays here, so the link comes back when the file comes back.</summary>
public sealed class LinksFile
{
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;
    public List<LinkEntry> Links { get; set; } = [];
}

public sealed class LinkEntry
{
    public string Id { get; set; } = "";
    public string FirstDocumentId { get; set; } = "";
    public string SecondDocumentId { get; set; } = "";
    public string Note { get; set; } = "";
}
