namespace Odysseum.Server.API.Models;

/// <summary>A saved state of the whole project. <c>Name</c> is null for versions the server saved on its own;
/// <c>Changes</c> counts the files that differ from the version before it.</summary>
public record VersionInfo(string Id, string? Name, bool Automatic, DateTime Saved, int Changes);

/// <summary>Saves the project as it is now. A name is required; automatic versions come from the monitor.</summary>
public record SaveVersionRequest(string Name);
