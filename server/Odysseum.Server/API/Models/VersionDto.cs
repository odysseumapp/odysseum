using Odysseum.Abstractions.History;

namespace Odysseum.Server.API.Models;

/// <summary>A saved state of the whole project. <c>Name</c> is null for versions the server saved on its own.
/// <c>Changes</c> counts the files that differ from the version before it.</summary>
public record VersionDto(string Id, string? Name, bool Automatic, DateTimeOffset Saved, int Changes)
{
    public static VersionDto FromVersion(ProjectVersion version) =>
        new(version.Id, version.Name, version.Automatic, version.Saved, version.Changes);
}
