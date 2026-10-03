using System.Text.Json;
using Odysseum.Abstractions.Folders;

namespace Odysseum.Server.Models;

/// <summary>A folder. <c>Path</c> is relative to the project; the project's top folder has the empty path. The storage
/// sets the path from <c>ParentFolderId</c> and <c>Name</c>.</summary>
public sealed record Folder(string Id, string ProjectId, string Name, string Path, string? ParentFolderId, IReadOnlyList<string> ChildIds,
    string? OwnDocumentId, string? PinnedView, IReadOnlyDictionary<string, JsonElement> Views, string ETag) : IFolder
{
    public bool IsRoot => ParentFolderId is null;
}
