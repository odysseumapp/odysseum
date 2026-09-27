using Odysseum.Abstractions.Documents;

namespace Odysseum.Server.Repositories.Disk.Formats;

/// <summary>The details of one document in the <see cref="FolderFile"/> of the folder the document is in. The
/// document's path is in <c>documents.json</c>.</summary>
public sealed class DocumentEntry
{
    public string Title { get; set; } = "";
    public string Synopsis { get; set; } = "";
    public string Notes { get; set; } = "";
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public int WordGoal { get; set; }
}
