namespace Odysseum.Abstractions.Projects;

/// <summary>A project as it was at one moment. It does not change; a later read gives a new one.</summary>
public interface IProject
{
    string Id { get; }
    /// <summary>The project's folder name in the workspace.</summary>
    string Name { get; }
    string Title { get; }
    int WordGoal { get; }
    int DefaultSceneWordGoal { get; }
    /// <summary>The ID of the project's top folder.</summary>
    string RootFolderId { get; }
    /// <summary>Changes when the project settings change. A change request must give the ETag it last saw.</summary>
    string ETag { get; }
    DateTimeOffset LastModified { get; }
    /// <summary>Problems found when the project was read, for example a file that is not UTF-8. Null when there are none.</summary>
    string? Warning { get; }
}
