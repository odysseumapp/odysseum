using System.Text.Json;
using System.Text.Json.Serialization;
using Odysseum.Server.API.Enums;

namespace Odysseum.Server.Services.Storage.Models;

public sealed class DocumentMetadata
{
    public string Path { get; set; } = "";
    public string Title { get; set; } = "";
    public string Synopsis { get; set; } = "";
    public string Notes { get; set; } = "";
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public int WordGoal { get; set; } = 1000;
    public double Order { get; set; }
    public string LastKnownHash { get; set; } = "";
    /// <summary>Ids of documents this one is linked to: characters, locations, threads, notes, anything. Links are
    /// undirected; the store keeps both sides listed, and readers treat either side as sufficient.</summary>
    public List<string> Links { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    internal DocumentMetadata Clone() => new()
    {
        Path = Path, Title = Title, Synopsis = Synopsis, Notes = Notes, Status = Status,
        WordGoal = WordGoal, Order = Order, LastKnownHash = LastKnownHash,
        Links = [.. Links], Extra = Extra is null ? null : new(Extra),
    };
}
