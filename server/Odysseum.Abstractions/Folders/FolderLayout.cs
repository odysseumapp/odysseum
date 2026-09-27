using System.Text.Json;

namespace Odysseum.Abstractions.Folders;

/// <summary>A change to how a folder is shown. The server does not know the views; it stores their names and settings.</summary>
public sealed record FolderLayout
{
    /// <summary>The view the folder opens in. Null unpins it.</summary>
    public string? PinnedView { get; init; }
    /// <summary>Settings by view name. A view given here gets these settings (a JSON object); a JSON null removes its
    /// settings; a view not given keeps its settings.</summary>
    public IReadOnlyDictionary<string, JsonElement>? Views { get; init; }
}
