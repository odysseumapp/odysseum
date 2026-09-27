using System.Text.Json.Serialization;

namespace Odysseum.Server.API.SignalR.Models;

public record DocumentChangedMessage(string ProjectId, string DocumentId, [property: JsonPropertyName("etag")] string ETag);
