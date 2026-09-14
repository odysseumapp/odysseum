using Odysseum.Server.API.Models;
using Odysseum.Server.Services;

namespace Odysseum.Server.Settings;

public class ProjectSettingsProvider : IProjectSettingsProvider
{
    private readonly string _slug;
    private readonly ProjectServices _services;
    private readonly ILogger<ProjectSettingsProvider> _logger;

    public ProjectSettingsProvider(string slug, ProjectServices services, ILogger<ProjectSettingsProvider> logger)
    {
        _slug = slug;
        _services = services;
        _logger = logger;
    }

    public Task<IProjectSettings> GetSettingsAsync() => _services.GetSettingsAsync();

    public Task<ProjectResponse> SaveSettingsAsync(IProjectSettings settings, string revision) => _services.UpdateSettingsAsync(settings, revision);

    public async Task DebugSettingsToLogAsync()
    {
        var settings = await GetSettingsAsync();
        _logger.LogInformation("Project {Slug}: title {Title}; word goal {WordGoal}; default scene goal {SceneGoal}",
            _slug, settings.Title, settings.WordGoal, settings.DefaultSceneWordGoal);
    }
}
