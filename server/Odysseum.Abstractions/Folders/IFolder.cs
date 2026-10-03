using System.Text.Json;
using Odysseum.Abstractions.Items;

namespace Odysseum.Abstractions.Folders;

/// <summary>A folder. Its ETag changes when the folder's name, place, children or layout change.</summary>
public interface IFolder : IProjectItem
{
    string Name { get; }
    /// <summary>Null for the project's top folder.</summary>
    string? ParentFolderId { get; }
    /// <summary>The IDs of the subfolders and documents in order. This is the only source of the order.</summary>
    IReadOnlyList<string> ChildIds { get; }
    /// <summary>The ID of the folder's own hidden document. It is never one of the children. The top folder has none.</summary>
    string? OwnDocumentId { get; }
    /// <summary>The name of the view the folder opens in, or null.</summary>
    string? PinnedView { get; }
    /// <summary>Settings by view name, each a JSON object. The server stores them without reading them.</summary>
    IReadOnlyDictionary<string, JsonElement> Views { get; }
}
