using System.Text.Json.Serialization;

namespace Odysseum.Server.API.Models;

/// <summary>A document's text. <c>ETag</c> is the document's ETag.</summary>
public record DocumentTextDto(string DocumentId, string Text, [property: JsonPropertyName("etag")] string ETag);
