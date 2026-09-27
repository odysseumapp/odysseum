using System.Text.Json.Serialization;

namespace Odysseum.Server.API.SignalR.Models;

public record FolderChangedMessage(string ProjectId, string FolderId, [property: JsonPropertyName("etag")] string ETag);
