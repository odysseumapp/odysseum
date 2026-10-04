using System.Text.Json;
using System.Text.RegularExpressions;

namespace Odysseum.Server.Plugins;

/// <summary><c>plugin.json</c> in a plugin's folder. The loader reads it before it loads any code, so that a disabled or
/// broken plugin is still listed. <c>Id</c> names the plugin in <c>disabledPlugins</c> and in <c>/plugins/{id}/</c>.
/// <c>Assembly</c> is the file name of the plugin's assembly in the folder. The client files are not named here: each
/// view the plugin adds names its own.</summary>
public sealed partial class PluginManifest
{
    public const string FileName = "plugin.json";
    private const long MaxBytes = 64 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Assembly { get; set; } = "";

    /// <summary>Reads and checks the manifest in the folder. Throws <see cref="InvalidDataException"/> when it is missing
    /// or not valid.</summary>
    public static PluginManifest Read(string folder)
    {
        var path = Path.Combine(folder, FileName);
        if (!File.Exists(path)) throw new InvalidDataException($"there is no {FileName}.");
        if (new FileInfo(path).Length > MaxBytes) throw new InvalidDataException($"{FileName} is larger than 64 KB.");
        PluginManifest? manifest;
        try { manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllBytes(path), Json); }
        catch (JsonException ex) { throw new InvalidDataException($"{FileName} is not valid JSON: {ex.Message}"); }
        if (manifest is null) throw new InvalidDataException($"{FileName} is empty.");

        if (!SafeId().IsMatch(manifest.Id ?? ""))
            throw new InvalidDataException("the id must use lowercase letters, digits, '.', '-' and '_', and be at most 64 characters.");
        if (string.IsNullOrWhiteSpace(manifest.Name)) throw new InvalidDataException("the name is missing.");
        if (string.IsNullOrWhiteSpace(manifest.Version)) throw new InvalidDataException("the version is missing.");
        if (string.IsNullOrEmpty(manifest.Assembly) || Path.GetFileName(manifest.Assembly) != manifest.Assembly
            || !manifest.Assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("the assembly must be the file name of a .dll in the plugin's folder.");
        if (!File.Exists(Path.Combine(folder, manifest.Assembly))) throw new InvalidDataException($"the assembly {manifest.Assembly} is missing.");
        return manifest;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$")] private static partial Regex SafeId();
}
