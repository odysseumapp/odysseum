namespace Odysseum.Abstractions;

public sealed record ProjectSettings
{
    public string Title { get; init; } = "Untitled manuscript";
    public int WordGoal { get; init; } = 50000;
    public int DefaultSceneWordGoal { get; init; } = 1000;
}
