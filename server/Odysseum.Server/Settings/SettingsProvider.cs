using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Odysseum.Server.Settings;

/// <summary>
/// Settings come from three layers: built-in defaults, then <c>server-settings.json</c> if present,
/// then <c>ODYSSEUM_*</c> environment variables, which always win so container deployments stay declarative.
/// </summary>
public class SettingsProvider : ISettingsProvider
{
    public const string SettingsFilename = "server-settings.json";
    private static readonly object SettingsLock = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { WriteIndented = true, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    private readonly ILogger<SettingsProvider> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _settingsPath;
    private readonly string _defaultWorkspace;
    private readonly bool _demoByDefault;
    private IServerSettings? _instance;

    public SettingsProvider(ILogger<SettingsProvider> logger, IConfiguration configuration, IWebHostEnvironment environment)
    {
        _logger = logger;
        _configuration = configuration;
        _settingsPath = configuration["ODYSSEUM_SETTINGS"] ?? Path.Combine(environment.ContentRootPath, SettingsFilename);
        _defaultWorkspace = Path.Combine(environment.ContentRootPath, "../../workspace");
        _demoByDefault = environment.IsDevelopment();
    }

    public IServerSettings GetSettings(bool copy = false)
    {
        lock (SettingsLock)
        {
            _instance ??= LoadSettings();
            return copy ? Clone(_instance) : _instance;
        }
    }

    public void SaveSettings(IServerSettings settings)
    {
        Validate(settings);
        lock (SettingsLock)
        {
            _instance = settings;
            var inCode = JsonSerializer.Serialize(settings, settings.GetType(), Json);
            var onDisk = File.Exists(_settingsPath) ? File.ReadAllText(_settingsPath) : string.Empty;
            if (onDisk.Equals(inCode, StringComparison.Ordinal)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.WriteAllText(_settingsPath, inCode);
            _logger.LogInformation("Settings saved to {Path}", _settingsPath);
        }
    }

    public void SaveSettings() => SaveSettings(GetSettings());

    public void DebugSettingsToLog()
    {
        var settings = GetSettings();
        _logger.LogInformation("Settings file: {Path} ({State})", _settingsPath, File.Exists(_settingsPath) ? "present" : "absent");
        _logger.LogInformation("Workspace: {Workspace}", settings.Workspace);
        _logger.LogInformation("Themes: {Themes}", settings.Themes);
        _logger.LogInformation("Project templates: {Templates}", settings.Templates);
        _logger.LogInformation("Demo seeding: {Demo}; scan interval: {Seconds}s; version after {VersionSeconds}s quiet; keys: {Keys}; password: {Password}; deleting default folders: {DefaultFolders}",
            settings.Demo, settings.ScanSeconds, settings.VersionSeconds, settings.Keys ?? "(default)", settings.PasswordRequired ? "configured" : "not set", settings.AllowDeletingDefaultFolders ? "allowed" : "blocked");
    }

    private IServerSettings LoadSettings()
    {
        var settings = new ServerSettings { Workspace = _defaultWorkspace, Demo = _demoByDefault };
        if (File.Exists(_settingsPath))
        {
            try
            {
                settings = JsonSerializer.Deserialize<ServerSettings>(File.ReadAllBytes(_settingsPath), Json) ?? settings;
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                _logger.LogWarning(ex, "Ignoring unreadable settings file {Path}", _settingsPath);
            }
        }
        settings.Workspace = _configuration["ODYSSEUM_WORKSPACE"] ?? settings.Workspace;
        settings.Password = _configuration["ODYSSEUM_PASSWORD"];
        settings.Demo = _configuration.GetValue("ODYSSEUM_DEMO", settings.Demo);
        settings.Keys = _configuration["ODYSSEUM_KEYS"] ?? settings.Keys;
        settings.WebUi = Path.GetFullPath(_configuration["ODYSSEUM_WEBUI"] ?? settings.WebUi
            ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_settingsPath))!, "webui"));
        settings.Themes = Path.GetFullPath(_configuration["ODYSSEUM_THEMES"] ?? settings.Themes
            ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_settingsPath))!, "themes"));
        settings.Templates = Path.GetFullPath(_configuration["ODYSSEUM_TEMPLATES"] ?? settings.Templates
            ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_settingsPath))!, "templates"));
        settings.ScanSeconds = _configuration.GetValue("ODYSSEUM_SCAN_SECONDS", settings.ScanSeconds);
        settings.VersionSeconds = _configuration.GetValue("ODYSSEUM_VERSION_SECONDS", settings.VersionSeconds);
        settings.AllowDeletingDefaultFolders = _configuration.GetValue("ODYSSEUM_ALLOW_DELETING_DEFAULT_FOLDERS", settings.AllowDeletingDefaultFolders);
        Validate(settings);
        settings.Workspace = Path.GetFullPath(settings.Workspace);
        return settings;
    }

    private static void Validate(IServerSettings settings)
    {
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(settings, new ValidationContext(settings), results, validateAllProperties: true))
            throw new InvalidOperationException("Invalid settings: " + string.Join(" ", results.Select(result => result.ErrorMessage)));
    }

    private static IServerSettings Clone(IServerSettings settings)
    {
        var clone = JsonSerializer.Deserialize<ServerSettings>(JsonSerializer.Serialize(settings, settings.GetType(), Json), Json)!;
        clone.Password = settings.Password;
        return clone;
    }
}
