using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Odysseum.Server.Services.Documents;
using Odysseum.Server.Services.Storage;

namespace Odysseum.Server.Services.Templates;

public sealed class ProjectTemplate
{
    public string Name { get; set; } = "";
    public TemplateSettings Settings { get; set; } = new();
    public List<TemplateFolder> Folders { get; set; } = [];
    public List<TemplateDocument> Documents { get; set; } = [];
}

public sealed class TemplateSettings
{
    public int WordGoal { get; set; } = 50000;
    public int DefaultSceneWordGoal { get; set; } = 1000;
}

public sealed class TemplateFolder
{
    public string Path { get; set; } = "";
    public string? PinnedView { get; set; }
    public List<string> ItemOrder { get; set; } = [];
    public string? GridFolder { get; set; }
}

public sealed class TemplateDocument
{
    public string Path { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Content { get; set; }
}

public sealed partial class TemplateStore(string root)
{
    public const string DefaultName = "Default";
    private const int MaxTemplates = 100;
    private const int MaxFolders = 1000;
    private const int MaxDocuments = 5000;
    private const long MaxFileBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public string Root { get; } = Path.GetFullPath(root);

    public static bool IsDefault(string name) => string.Equals(name, DefaultName, StringComparison.OrdinalIgnoreCase);

    public const string DefaultStylesheet = "```css\n/* Only .name blocks count. .normal is what untagged text looks like;\n"
        + "   every other .name is a style you can pick from the toolbar,\n"
        + "   for a whole block or for selected text. */\n"
        + ".normal {\n  font-family: Georgia, serif;\n}\n\n.letter {\n  font-style: italic;\n}\n```\n";

    public static ProjectTemplate Default() => new()
    {
        Name = DefaultName,
        Folders =
        [
            new() { Path = "", ItemOrder = [.. ProjectLibrary.DefaultFolders.Select(name => "folder:" + name)] },
            new() { Path = "Manuscript" },
            new() { Path = "Manuscript/Chapter 01" },
            .. ProjectLibrary.DefaultFolders.Skip(1).Select(name => new TemplateFolder { Path = name }),
        ],
        Documents =
        [
            new() { Path = "Manuscript/Chapter 01/Scene 01.md", Title = "Scene 01" },
            new() { Path = "Styles/Default.md", Title = "Default", Content = DefaultStylesheet },
        ],
    };

    public void EnsureDefault()
    {
        if (!File.Exists(PathFor(DefaultName))) Write(Default());
    }

    public IReadOnlyList<ProjectTemplate> List()
    {
        var templates = new List<ProjectTemplate>();
        if (Directory.Exists(Root))
        {
            foreach (var file in Directory.EnumerateFiles(Root, "*.json").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (Read(file, Path.GetFileNameWithoutExtension(file)) is { } template) templates.Add(template);
            }
        }
        if (!templates.Any(template => IsDefault(template.Name))) templates.Insert(0, Default());
        return templates;
    }

    public ProjectTemplate Get(string name) => Read(PathFor(ValidName(name)), name)
        ?? (IsDefault(name) ? Default() : throw new WorkspaceException(404, $"There is no project template called '{name}'."));

    public ProjectTemplate Save(ProjectTemplate template)
    {
        template.Name = ValidName(template.Name);
        Validate(template);
        Directory.CreateDirectory(Root);
        if (!File.Exists(PathFor(template.Name)) && Directory.EnumerateFiles(Root, "*.json").Count() >= MaxTemplates)
            throw new WorkspaceException(409, $"This workspace already holds {MaxTemplates} project templates. Delete one before saving another.");
        Write(template);
        return template;
    }

    public void Delete(string name)
    {
        var path = PathFor(ValidName(name));
        if (IsDefault(name)) { File.Delete(path); EnsureDefault(); return; }
        if (!File.Exists(path)) throw new WorkspaceException(404, $"There is no project template called '{name}'.");
        File.Delete(path);
    }

    private string PathFor(string name) => Path.Combine(Root, name + ".json");

    private void Write(ProjectTemplate template)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(template, Json);
        if (bytes.Length > MaxFileBytes) throw new WorkspaceException(413, "This project is too large to keep as a template.");
        Directory.CreateDirectory(Root);
        File.WriteAllBytes(PathFor(template.Name), bytes);
    }

    private static ProjectTemplate? Read(string path, string fallbackName)
    {
        if (!File.Exists(path)) return null;
        try
        {
            if (new FileInfo(path).Length > MaxFileBytes) return null;
            var template = JsonSerializer.Deserialize<ProjectTemplate>(File.ReadAllBytes(path), Json);
            if (template is null) return null;
            template.Name = fallbackName;
            Validate(template);
            return template;
        }
        catch (Exception ex) when (ex is JsonException or WorkspaceException or IOException) { return null; }
    }

    public static string ValidName(string? name)
    {
        name = name?.Trim() ?? "";
        if (name.Length is 0 or > 60) throw new WorkspaceException(400, "Use a template name between 1 and 60 characters.");
        if (name.StartsWith('.') || name.EndsWith('.') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.Contains('/') || name.Contains('\\') || ReservedName().IsMatch(name))
            throw new WorkspaceException(400, "A template name cannot contain \\ / : * ? \" < > | or start with a dot.");
        return name;
    }

    private static void Validate(ProjectTemplate template)
    {
        static WorkspaceException Invalid(string message) => new(400, message);
        template.Settings ??= new();
        template.Folders ??= [];
        template.Documents ??= [];
        if (template.Settings.WordGoal is < 0 or > 10000000 || template.Settings.DefaultSceneWordGoal is < 0 or > 10000000)
            throw Invalid("The template has an invalid word goal.");
        if (template.Folders.Count > MaxFolders || template.Documents.Count > MaxDocuments)
            throw Invalid("The template holds too many folders or documents.");

        var paths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var folder in template.Folders)
        {
            if (folder?.Path is null || (folder.Path != "" && !ProjectFileStore.IsSafePath(folder.Path)) || !paths.Add(folder.Path))
                throw Invalid($"The template has a folder it cannot create: '{folder?.Path}'.");
            if (folder.PinnedView is not (null or "write" or "board" or "outline" or "grid")) throw Invalid("The template pins a view that does not exist.");
            folder.ItemOrder = [.. (folder.ItemOrder ?? []).Where(key => !string.IsNullOrWhiteSpace(key)).Distinct()];
        }
        foreach (var document in template.Documents)
        {
            if (document?.Path is null || !ProjectFileStore.IsSafePath(document.Path) || !DocumentRules.IsDocument(document.Path) || !paths.Add(document.Path))
                throw Invalid($"The template has a document it cannot create: '{document?.Path}'.");
            document.Title = DocumentRules.ValidateTitle(string.IsNullOrWhiteSpace(document.Title) ? Path.GetFileNameWithoutExtension(document.Path).TrimStart('.') : document.Title);
        }
    }

    [GeneratedRegex(@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase)] private static partial Regex ReservedName();
}
