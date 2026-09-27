using System.Text.Json;

namespace Odysseum.Server.API.Models;

/// <summary><c>PinnedView</c> replaces the pinned view; null unpins it. Each view in <c>Views</c> gets the settings given
/// (a JSON object), a JSON null removes that view's settings, and views not given keep theirs.</summary>
public record UpdateFolderLayoutRequest(string? PinnedView, Dictionary<string, JsonElement>? Views);
