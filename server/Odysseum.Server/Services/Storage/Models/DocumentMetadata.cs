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
    /// <summary>Ids of character documents attached to this document (meaningful for scenes).</summary>
    public List<string> Characters { get; set; } = [];
    public List<string> Locations { get; set; } = [];
    /// <summary>Ids of thread documents this document belongs to; shown at the intersection in each folder's Threads view.</summary>
    public List<string> Threads { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    internal DocumentMetadata Clone() => new()
    {
        Path = Path, Title = Title, Synopsis = Synopsis, Notes = Notes, Status = Status,
        WordGoal = WordGoal, Order = Order, LastKnownHash = LastKnownHash,
        Characters = [.. Characters], Locations = [.. Locations], Threads = [.. Threads], Extra = Extra is null ? null : new(Extra),
    };
}
