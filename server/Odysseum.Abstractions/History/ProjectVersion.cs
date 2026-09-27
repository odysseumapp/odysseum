namespace Odysseum.Abstractions.History;

/// <summary>A saved state of a project branch. <c>Label</c> is null for versions the server saved on its own.
/// <c>Changes</c> counts the files that differ from the version before it.</summary>
public sealed record ProjectVersion(string Id, string? Label, bool Automatic, DateTimeOffset Saved, int Changes);
