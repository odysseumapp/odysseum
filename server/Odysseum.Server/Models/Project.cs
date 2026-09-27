using Odysseum.Abstractions.Projects;

namespace Odysseum.Server.Models;

/// <summary>A project. <c>Id</c> is also the ID of its top folder.</summary>
public sealed record Project(string Id, string Name, string Title, int WordGoal, int DefaultSceneWordGoal, string ETag,
    DateTimeOffset LastModified, string? Warning) : IProject
{
    public string RootFolderId => Id;
}
