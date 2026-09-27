using ProjectSettings = Odysseum.Server.Settings.ProjectSettings;

namespace Odysseum.Server.Models;

/// <summary>What storage loads and saves: the settings, the folder tree with document summaries, and the revision.
/// The tree is not placed yet; <see cref="Project"/> places it.</summary>
public sealed record ProjectData(string Id, ProjectSettings Settings, string Revision, Folder Root, string? Warning);
