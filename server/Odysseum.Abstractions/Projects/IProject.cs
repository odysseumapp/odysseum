using Odysseum.Abstractions.Folders;

namespace Odysseum.Abstractions.Projects;

public interface IProject
{
    string Id { get; }
    string Title { get; }
    int WordGoal { get; }
    int DefaultSceneWordGoal { get; }
    IFolder Root { get; }
    string Revision { get; }
    DateTimeOffset LastModified { get; }
}
