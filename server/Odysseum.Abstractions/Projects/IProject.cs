using Odysseum.Abstractions.Documents;
using Odysseum.Abstractions.Folders;
using Odysseum.Abstractions.Items;

namespace Odysseum.Abstractions.Projects;

/// <summary>A project as it was at one moment. It does not change; a later read gives a new one.</summary>
public interface IProject
{
    ProjectBranch Branch { get; }
    /// <summary>The project's folder name in the workspace.</summary>
    string Name { get; }
    string Id { get; }
    string Title { get; }
    int WordGoal { get; }
    int DefaultSceneWordGoal { get; }
    string Revision { get; }
    string? Warning { get; }
    IFolder Root { get; }
    IFolder? Folder(string id);
    IDocument? Document(string id);
    /// <summary>The project-relative path of an item of this project. The root has the empty path.</summary>
    string PathOf(IItem item);
}
