using System.Text.Json;
using System.Text.RegularExpressions;

namespace Odysseum.Server.Services.Themes;

public sealed class Theme
{
    public string Name { get; set; } = "";
    public Dictionary<string, string> Colors { get; set; } = [];
}

public sealed partial class ThemeStore(string root)
{
    public static readonly string[] Roles = ["primary", "secondary", "success", "info", "warning", "error", "neutral"];
    public static readonly string[] Palettes =
    [
        "red", "orange", "amber", "yellow", "lime", "green", "emerald", "teal", "cyan", "sky", "blue",
        "indigo", "violet", "purple", "fuchsia", "pink", "rose",
        "slate", "gray", "zinc", "neutral", "stone", "mauve", "olive", "mist", "taupe",
    ];
    private const int MaxThemes = 200;
    private const long MaxFileBytes = 64 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public string Root { get; } = Path.GetFullPath(root);

    public IReadOnlyList<Theme> List()
    {
        if (!Directory.Exists(Root)) return [];
        var themes = new List<Theme>();
        foreach (var file in Directory.EnumerateFiles(Root, "*.json").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (Read(file, Path.GetFileNameWithoutExtension(file)) is { } theme) themes.Add(theme);
        }
        return themes;
    }

    public Theme Get(string name) => Read(PathFor(ValidName(name)), name) ?? throw new WorkspaceException(404, $"There is no theme called '{name}'.");

    public Theme Save(string name, Dictionary<string, string>? colors)
    {
        var theme = new Theme { Name = ValidName(name), Colors = ValidColors(colors) };
        Directory.CreateDirectory(Root);
        var path = PathFor(theme.Name);
        if (!File.Exists(path) && Directory.EnumerateFiles(Root, "*.json").Count() >= MaxThemes)
            throw new WorkspaceException(409, $"This workspace already holds {MaxThemes} themes. Delete one before saving another.");
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(theme, Json));
        return theme;
    }

    public void Delete(string name)
    {
        var path = PathFor(ValidName(name));
        if (!File.Exists(path)) throw new WorkspaceException(404, $"There is no theme called '{name}'.");
        File.Delete(path);
    }

    private string PathFor(string name) => Path.Combine(Root, name + ".json");

    private static Theme? Read(string path, string fallbackName)
    {
        if (!File.Exists(path)) return null;
        try
        {
            if (new FileInfo(path).Length > MaxFileBytes) return null;
            var theme = JsonSerializer.Deserialize<Theme>(File.ReadAllBytes(path), Json);
            if (theme is null) return null;
            theme.Name = fallbackName;
            theme.Colors = ValidColors(theme.Colors);
            return theme;
        }
        catch (Exception ex) when (ex is JsonException or WorkspaceException) { return null; }
    }

    public static string ValidName(string? name)
    {
        name = name?.Trim() ?? "";
        if (name.Length is 0 or > 60) throw new WorkspaceException(400, "Use a theme name between 1 and 60 characters.");
        if (name.StartsWith('.') || name.EndsWith('.') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.Contains('/') || name.Contains('\\') || ReservedName().IsMatch(name))
            throw new WorkspaceException(400, "A theme name cannot contain \\ / : * ? \" < > | or start with a dot.");
        return name;
    }

    private static Dictionary<string, string> ValidColors(Dictionary<string, string>? colors)
    {
        colors ??= [];
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var role in Roles)
        {
            if (!colors.TryGetValue(role, out var palette) || palette is null)
                throw new WorkspaceException(400, $"The theme is missing a colour for '{role}'.");
            if (!Palettes.Contains(palette, StringComparer.Ordinal))
                throw new WorkspaceException(400, $"'{palette}' is not a colour this theme can use.");
            result[role] = palette;
        }
        return result;
    }

    [GeneratedRegex(@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase)] private static partial Regex ReservedName();
}
