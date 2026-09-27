using System.Text.Json;
using Odysseum.Abstractions.Folders;

namespace Odysseum.Server.Models;

public sealed record Folder(string Id, string ProjectId, string Name, string? ParentFolderId, IReadOnlyList<string> ChildIds,
    string? OwnDocumentId, string? PinnedView, IReadOnlyDictionary<string, JsonElement> Views, string ETag) : IFolder
{
    public bool IsRoot => ParentFolderId is null;
}
