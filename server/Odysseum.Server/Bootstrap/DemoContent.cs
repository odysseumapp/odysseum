using Odysseum.Server.Settings;

namespace Odysseum.Server.Bootstrap;

public static class DemoContent
{
    public const string ProjectSlug = "Sample manuscript";

    /// <summary>Copies the sample project into a completely empty workspace. Existing content is never touched.</summary>
    public static void Seed(IServerSettings settings)
    {
        Directory.CreateDirectory(settings.Workspace);
        if (!settings.Demo || Directory.EnumerateFileSystemEntries(settings.Workspace).Any()) return;
        var examples = Path.Combine(AppContext.BaseDirectory, "Examples");
        if (!Directory.Exists(examples)) return;
        var project = Path.Combine(settings.Workspace, ProjectSlug);
        foreach (var file in Directory.EnumerateFiles(examples, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(project, Path.GetRelativePath(examples, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }
    }
}
