using Odysseum.Abstractions.Projects;
using System.Text.Json.Serialization;

namespace Odysseum.Server.API.Models;

public record ProjectDto(string Id, string Name, string Title, int WordGoal, int DefaultSceneWordGoal, string RootFolderId,
    DateTimeOffset LastModified, string? Warning, [property: JsonPropertyName("etag")] string ETag)
{
    public static ProjectDto FromProject(IProject project) => new(project.Id, project.Name, project.Title, project.WordGoal,
        project.DefaultSceneWordGoal, project.RootFolderId, project.LastModified, project.Warning, project.ETag);
}
