using System.Text.Json;
using Odysseum.Abstractions.Documents;
using Odysseum.Abstractions.Items;

namespace Odysseum.Abstractions.Folders;

public interface IFolder : IItem
{
    /// <summary>The name of the view the folder opens in, or null.</summary>
    string? PinnedView { get; }
    /// <summary>Settings by view name, each a JSON object. The server stores them without reading them.</summary>
    IReadOnlyDictionary<string, JsonElement> Views { get; }
    /// <summary>Subfolders and documents in order. This is the only source of the order.</summary>
    IReadOnlyList<IItem> Children { get; }
    /// <summary>The folder's own hidden document. It is never one of the children. The root has none.</summary>
    IDocument? OwnDocument { get; }
}
