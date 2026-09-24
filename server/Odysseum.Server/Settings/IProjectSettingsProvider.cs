using Odysseum.Server.API.Models;

namespace Odysseum.Server.Settings;

public interface IProjectSettingsProvider
{
    Task<IProjectSettings> GetSettingsAsync();

    Task<ProjectResponse> SaveSettingsAsync(IProjectSettings settings, string revision);

    Task DebugSettingsToLogAsync();
}
