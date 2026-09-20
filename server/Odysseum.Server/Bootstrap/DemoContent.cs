using Odysseum.Server.Settings;

namespace Odysseum.Server.Bootstrap;

public static class DemoContent
{
    public const string ProjectSlug = "Sample manuscript";

    /// <summary>Copies the sample project into the workspace whenever it is missing. Delete the folder to get a fresh
    /// copy after the examples change; an existing sample, and every other project, is never touched.</summary>
    public static void Seed(IServerSettings settings)
    {
        Directory.CreateDirectory(settings.Workspace);
        var project = Path.Combine(settings.Workspace, ProjectSlug);
        if (!settings.Demo || Directory.Exists(project)) return;
        var examples = Path.Combine(AppContext.BaseDirectory, "Examples");
        if (!Directory.Exists(examples)) return;
        foreach (var file in Directory.EnumerateFiles(examples, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(project, Path.GetRelativePath(examples, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }
    }
}
