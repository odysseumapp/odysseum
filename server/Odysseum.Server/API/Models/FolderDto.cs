using System.Text.Json;
using Odysseum.Abstractions.Folders;
using System.Text.Json.Serialization;

namespace Odysseum.Server.API.Models;

/// <summary><c>ChildIds</c> lists the subfolders and documents in order. <c>Views</c> holds settings by view name; the
/// server stores them without reading them.</summary>
public record FolderDto(string Id, string ProjectId, string Name, string? ParentFolderId, IReadOnlyList<string> ChildIds,
    string? OwnDocumentId, string? PinnedView, IReadOnlyDictionary<string, JsonElement> Views, [property: JsonPropertyName("etag")] string ETag)
{
    public static FolderDto FromFolder(IFolder folder) => new(folder.Id, folder.ProjectId, folder.Name, folder.ParentFolderId,
        folder.ChildIds, folder.OwnDocumentId, folder.PinnedView, folder.Views, folder.ETag);
}
