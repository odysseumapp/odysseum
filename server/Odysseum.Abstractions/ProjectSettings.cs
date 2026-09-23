namespace Odysseum.Abstractions;

/// <summary>Per-project preferences. Validation belongs to whoever accepts them from a user; the store persists what it is given.</summary>
public sealed record ProjectSettings
{
    public string Title { get; init; } = "Untitled manuscript";
    /// <summary>Manuscript word goal shown in the binder footer.</summary>
    public int WordGoal { get; init; } = 50000;
    /// <summary>Word goal given to newly created or newly discovered scenes.</summary>
    public int DefaultSceneWordGoal { get; init; } = 1000;
}
