using System.Text.Json;

namespace Odysseum.Server.API.Models;

/// <summary>The project's top folder has the empty path. <c>Children</c> are the names of the subfolders and documents
/// in order. In <c>Views</c>, a setting that names a folder holds the folder's path.</summary>
public record TemplateFolderDto(string Path, string? PinnedView, IReadOnlyList<string> Children, IReadOnlyDictionary<string, JsonElement>? Views);
