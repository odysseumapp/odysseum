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
    public List<string> Links { get; set; } = [];
    public Dictionary<string, string> LinkNotes { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    internal DocumentMetadata Clone() => new()
    {
        Path = Path, Title = Title, Synopsis = Synopsis, Notes = Notes, Status = Status,
        WordGoal = WordGoal, Order = Order, LastKnownHash = LastKnownHash,
        Links = [.. Links], LinkNotes = new(LinkNotes), Extra = Extra is null ? null : new(Extra),
    };
}
