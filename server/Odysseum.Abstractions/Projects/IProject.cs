using Odysseum.Abstractions.Items;

namespace Odysseum.Abstractions.Projects;

/// <summary>A project. Its ETag changes when the project settings change.</summary>
public interface IProject : IItem
{
    /// <summary>The project's folder name in the workspace.</summary>
    string Name { get; }
    string Title { get; }
    int WordGoal { get; }
    int DefaultSceneWordGoal { get; }
    /// <summary>The ID of the project's top folder.</summary>
    string RootFolderId { get; }
    DateTimeOffset LastModified { get; }
    /// <summary>Problems found when the project was read, for example a file that is not UTF-8. Null when there are none.</summary>
    string? Warning { get; }
}
