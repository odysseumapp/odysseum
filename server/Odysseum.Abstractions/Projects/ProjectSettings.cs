namespace Odysseum.Abstractions.Projects;

public sealed record ProjectSettings
{
    public string? Title { get; init; }
    public int? WordGoal { get; init; }
    public int? DefaultSceneWordGoal { get; init; }
}
