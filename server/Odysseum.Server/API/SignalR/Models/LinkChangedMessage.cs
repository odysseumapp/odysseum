using System.Text.Json.Serialization;

namespace Odysseum.Server.API.SignalR.Models;

public record LinkChangedMessage(string ProjectId, string LinkId, [property: JsonPropertyName("etag")] string ETag);
