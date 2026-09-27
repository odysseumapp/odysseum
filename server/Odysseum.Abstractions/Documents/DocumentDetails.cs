namespace Odysseum.Abstractions.Documents;

public sealed record DocumentDetails
{
    public string? Title { get; init; }
    public string? Synopsis { get; init; }
    public string? Notes { get; init; }
    public DocumentStatus? Status { get; init; }
    public int? WordGoal { get; init; }
    public IReadOnlyList<string>? Links { get; init; }
    public IReadOnlyDictionary<string, string>? LinkNotes { get; init; }
}
