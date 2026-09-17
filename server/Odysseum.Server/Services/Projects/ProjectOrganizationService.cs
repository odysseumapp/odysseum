using Odysseum.Server.API.Models;
using Odysseum.Server.Settings;
using static Odysseum.Server.Services.Documents.DocumentRules;
using static Odysseum.Server.Services.Storage.ContentRevision;

namespace Odysseum.Server.Services.Projects;

/// <summary>Updates scene details, project settings, and manuscript order using candidate manifests.</summary>
internal sealed class ProjectOrganizationService(ProjectState state)
{
    public async Task UpdateMetadataAsync(string id, MetadataRequest request)
    {
        Check(state.Revision, request.Revision);
        var document = state.Find(id);
        var candidate = state.Manifest.Clone();
        var metadata = candidate.Documents[id];
        var title = ValidateTitle(request.Title);
        if (!Enum.IsDefined(request.Status)) throw new WorkspaceException(400, "Unknown document status.");
        if (request.WordGoal is < 0 or > 10000000) throw new WorkspaceException(400, "Invalid word goal.");
        if (request.Synopsis.Length > 20000 || request.Notes.Length > 100000) throw new WorkspaceException(400, "Notes are too long.");
        metadata.Title = title;
        metadata.Synopsis = request.Synopsis;
        metadata.Notes = request.Notes;
        metadata.Status = request.Status;
        metadata.WordGoal = request.WordGoal;
        if (request.Links is not null)
        {
            var links = ValidateLinks(request.Links, id);
            // Links are undirected: the other documents list this one too, so they change with it.
            var before = Links.Of(candidate, id);
            foreach (var removed in before.Except(links))
            {
                candidate.Documents[removed].Links.Remove(id);
                // A note belongs to its link and goes with it.
                candidate.Documents[removed].LinkNotes.Remove(id);
                metadata.LinkNotes.Remove(removed);
            }
            foreach (var added in links.Except(before)) if (!candidate.Documents[added].Links.Contains(id)) candidate.Documents[added].Links.Add(id);
            metadata.Links = links;
        }
        if (request.LinkNotes is not null)
        {
            var current = Links.Of(candidate, id);
            if (request.LinkNotes.Values.Any(note => note is null || note.Length > 2000)) throw new WorkspaceException(400, "A link note is too long.");
            // Notes are shared like the links they sit on: both ends carry the same text, and an empty note is no note.
            // A note for a document that is not linked is dropped rather than refused, so a replayed edit survives an unlink.
            foreach (var other in current)
            {
                var note = request.LinkNotes.GetValueOrDefault(other)?.Trim() ?? "";
                if (note.Length == 0) { metadata.LinkNotes.Remove(other); candidate.Documents[other].LinkNotes.Remove(id); }
                else metadata.LinkNotes[other] = candidate.Documents[other].LinkNotes[id] = note;
            }
        }
        await state.CommitManifestAsync(candidate, state.Revision);
        state.PublishChanges();
    }

    public async Task UpdateSettingsAsync(ProjectSettings validated, string revision)
    {
        Check(state.Revision, revision);
        var candidate = state.Manifest.Clone();
        candidate.Settings = validated;
        await state.CommitManifestAsync(candidate, state.Revision);
        state.PublishChanges();
    }

    private List<string> ValidateLinks(string[] ids, string self)
    {
        if (ids.Length > 200) throw new WorkspaceException(400, "Too many links attached.");
        var distinct = ids.Distinct().ToList();
        if (distinct.Any(id => id is null || id == self || !state.Documents.ContainsKey(id)))
            throw new WorkspaceException(400, "One of the linked documents no longer exists.");
        return distinct;
    }

    public async Task ReorderAsync(ReorderRequest request)
    {
        Check(state.Revision, request.Revision);
        // Folders' own documents may be left out; they keep their relative order after the listed ones.
        var required = state.Documents.Values.Where(d => !IsFolderDocument(d.Path)).Select(d => d.Id);
        if (request.Ids.Distinct().Count() != request.Ids.Length || request.Ids.Any(id => !state.Documents.ContainsKey(id))
            || required.Any(id => !request.Ids.Contains(id))) throw new WorkspaceException(400, "The document list changed. Refresh and try again.");
        var candidate = state.Manifest.Clone();
        var order = 0;
        foreach (var id in request.Ids) candidate.Documents[id].Order = order++;
        foreach (var id in state.Documents.Keys.Where(id => !request.Ids.Contains(id)).OrderBy(id => candidate.Documents[id].Order))
            candidate.Documents[id].Order = order++;
        await state.CommitManifestAsync(candidate, state.Revision);
        state.PublishChanges();
    }
}
