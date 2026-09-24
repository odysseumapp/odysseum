namespace Odysseum.Abstractions.Folders;

public sealed record FolderLayout
{
    public FolderView? PinnedView { get; init; }
    public string? GridFolderId { get; init; }
}
