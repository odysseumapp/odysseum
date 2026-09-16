using Odysseum.Server.API.Enums;
using Odysseum.Server.API.Models;
using Odysseum.Server.Services.Storage.Models;
using Odysseum.Server.Settings;
using static Odysseum.Server.Services.Documents.DocumentRules;
using static Odysseum.Server.Services.Documents.MarkdownDocumentCodec;

namespace Odysseum.Server.Services.Projects;

/// <summary>Builds detached responses, search results, and manuscript exports from the current project state.
/// The coordinator refreshes state and holds the operation lock while these synchronous queries run.</summary>
internal sealed class ProjectQueries(ProjectState state)
{
    public ProjectResponse GetProject() => new(state.Manifest.Id, state.Manifest.Settings.Clone(), state.Revision,
        Ordered().Select(Summary).ToArray(), state.Warning, GetFolders());
    // Links are reported undirected, so the summary needs every document that points back at this one.
    private Dictionary<string, List<string>> Reverse() => Links.Reverse(state.Manifest, state.Documents.Keys);

    private FolderSummary[] GetFolders() => state.Manifest.FolderManifests
        .Prepend(new KeyValuePair<string, FolderManifest>("", state.Manifest))
        .Select(pair => new FolderSummary(pair.Value.Id, pair.Key,
            pair.Key == "" ? state.FolderName : Path.GetFileName(pair.Key),
            pair.Key == "" ? null : Path.GetDirectoryName(pair.Key)?.Replace('\\', '/') ?? "",
            pair.Value.PinnedView, [.. pair.Value.ItemOrder], pair.Value.GridFolder))
        .ToArray();
    public DocumentContent GetDocument(string id) => Content(state.Find(id));
    public IReadOnlyList<DocumentContent> GetAllDocuments() { var reverse = Reverse(); return Ordered().Select(d => new DocumentContent(Summary(d, reverse), d.Body)).ToArray(); }
    public IProjectSettings GetSettings() => state.Manifest.Settings.Clone();

    public string Export()
    {
        return $"# {state.Manifest.Settings.Title}\n\n" + string.Join("\n\n---\n\n", Ordered().Where(x => KindOf(x.Path) == DocumentKind.Scene).Select(x =>
            $"## {state.Manifest.Documents[x.Id].Title}\n\n{x.Body.Trim()}")) + "\n";
    }

    public IReadOnlyList<SearchResult> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var results = new List<SearchResult>();
        var reverse = Reverse();
        foreach (var document in Ordered())
        {
            var metadata = state.Manifest.Documents[document.Id];
            var position = document.Body.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (position < 0 && !metadata.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !metadata.Synopsis.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !metadata.Notes.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            var start = Math.Max(0, position - 55);
            results.Add(new(Summary(document, reverse), document.Body.Substring(start, Math.Min(180, document.Body.Length - start)).Replace('\n', ' ')));
            if (results.Count == 50) break;
        }
        return results;
    }

    private DocumentContent Content(DiskDocument document) => new(Summary(document), document.Body);
    private DocumentSummary Summary(DiskDocument d) => Summary(d, Reverse());
    private DocumentSummary Summary(DiskDocument d, Dictionary<string, List<string>> reverse)
    {
        var m = state.Manifest.Documents[d.Id];
        var links = m.Links.Where(state.Documents.ContainsKey).Concat(reverse.GetValueOrDefault(d.Id) ?? []).Distinct().ToArray();
        return new(d.Id, d.Path, m.Title, Path.GetDirectoryName(d.Path)?.Replace('\\', '/') ?? "",
            m.Synopsis, m.Notes, m.Status, m.WordGoal, m.Order, CountWords(d.Body), d.Revision, d.Modified, KindOf(d.Path), links);
    }
    private IEnumerable<DiskDocument> Ordered() => state.Documents.Values.OrderBy(x => state.Manifest.Documents[x.Id].Order).ThenBy(x => x.Path, StringComparer.Ordinal);
}
