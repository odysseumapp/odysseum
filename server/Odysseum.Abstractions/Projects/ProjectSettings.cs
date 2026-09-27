namespace Odysseum.Abstractions.Projects;

/// <summary>All settings of a project. A save replaces all of them.</summary>
public sealed record ProjectSettings(string Title, int WordGoal, int DefaultSceneWordGoal);
