using System.Text.Json;
using System.Text.Json.Serialization;
using Odysseum.Abstractions.Documents;

namespace Odysseum.Server.Repositories.Manifests;

public sealed class DocumentMetadata
{
    public string Path { get; set; } = "";
    public string Title { get; set; } = "";
    public string Synopsis { get; set; } = "";
    public string Notes { get; set; } = "";
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public int WordGoal { get; set; } = 1000;
    [JsonIgnore] internal double? LegacyOrder { get; set; }
    public string LastKnownHash { get; set; } = "";
    /// <summary>Links as earlier releases stored them, on both documents. Read once and moved to <c>links.json</c>; never written.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Links { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, string>? LinkNotes { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    internal DocumentMetadata Clone() => new()
    {
        Path = Path, Title = Title, Synopsis = Synopsis, Notes = Notes, Status = Status,
        WordGoal = WordGoal, LegacyOrder = LegacyOrder, LastKnownHash = LastKnownHash,
        Links = Links is null ? null : [.. Links], LinkNotes = LinkNotes is null ? null : new(LinkNotes), Extra = Extra is null ? null : new(Extra),
    };
}
