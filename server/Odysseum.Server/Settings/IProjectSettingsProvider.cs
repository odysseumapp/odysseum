using Odysseum.Server.API.Models;

namespace Odysseum.Server.Settings;

/// <summary>Settings for one open project. Reads and writes go through the project's services so revisions stay consistent.</summary>
public interface IProjectSettingsProvider
{
    Task<IProjectSettings> GetSettingsAsync();

    /// <summary><paramref name="revision"/> is the project metadata revision the caller last saw; a stale value is rejected with 409.</summary>
    Task<ProjectResponse> SaveSettingsAsync(IProjectSettings settings, string revision);

    Task DebugSettingsToLogAsync();
}
