using System.Text.Json.Serialization;
using Odysseum.Abstractions.Changes;

namespace Odysseum.Server.API.SignalR.Models;

/// <summary>One changed item. <c>etag</c> is null for a removed item.</summary>
public record ChangeMessage(ItemType Type, ChangeKind Kind, string Id, [property: JsonPropertyName("etag")] string? ETag);
