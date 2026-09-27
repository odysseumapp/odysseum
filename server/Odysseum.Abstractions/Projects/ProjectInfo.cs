namespace Odysseum.Abstractions.Projects;

/// <summary>A project as listed in the workspace. <c>Name</c> is the project folder name, used in URLs.</summary>
public sealed record ProjectInfo(string Name, string Title, string Id, DateTimeOffset LastModified);
