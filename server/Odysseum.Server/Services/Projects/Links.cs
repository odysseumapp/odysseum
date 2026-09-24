using Odysseum.Server.Services.Storage.Models;

namespace Odysseum.Server.Services.Projects;

internal static class Links
{
    public static List<string> Of(ProjectManifest manifest, string id)
    {
        var own = manifest.Documents.TryGetValue(id, out var metadata) ? metadata.Links : [];
        return own.Concat(manifest.Documents.Where(pair => pair.Value.Links.Contains(id)).Select(pair => pair.Key)).Distinct().ToList();
    }

    public static Dictionary<string, List<string>> Reverse(ProjectManifest manifest, IEnumerable<string> present)
    {
        var ids = present.ToHashSet(StringComparer.Ordinal);
        var reverse = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (source, metadata) in manifest.Documents)
        {
            if (!ids.Contains(source)) continue;
            foreach (var target in metadata.Links)
                if (ids.Contains(target)) (reverse.TryGetValue(target, out var list) ? list : reverse[target] = []).Add(source);
        }
        return reverse;
    }
}
