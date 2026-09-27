using System.Text.Json.Serialization;

namespace Odysseum.Server.API.SignalR.Models;

public record ProjectChangedMessage(string ProjectId, [property: JsonPropertyName("etag")] string ETag);
