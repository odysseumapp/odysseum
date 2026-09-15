using Odysseum.Server.API.Enums;
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
        if (request.Characters is not null) metadata.Characters = ValidateLinks(request.Characters, DocumentKind.Character, "characters");
        if (request.Locations is not null) metadata.Locations = ValidateLinks(request.Locations, DocumentKind.Location, "locations");
        if (request.ArcPositions is not null)
        {
            if (request.ArcPositions.Count > 0 && KindOf(document.Path) != DocumentKind.Beat)
                throw new WorkspaceException(400, "Only beats can be placed on arcs.");
            ValidateLinks(request.ArcPositions.Keys.ToArray(), DocumentKind.Arc, "arcs");
            if (request.ArcPositions.Values.Any(position => !double.IsFinite(position) || position is < 0 or > 10000))
                throw new WorkspaceException(400, "Arc positions must be between 0 and 10000.");
            metadata.ArcPositions = new(request.ArcPositions);
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

    private List<string> ValidateLinks(string[] ids, DocumentKind kind, string label)
    {
        if (ids.Length > 200) throw new WorkspaceException(400, $"Too many {label} attached.");
        var distinct = ids.Distinct().ToList();
        if (distinct.Any(id => id is null || !state.Documents.TryGetValue(id, out var document) || KindOf(document.Path) != kind))
            throw new WorkspaceException(400, $"One of the attached {label} no longer exists.");
        return distinct;
    }

    public async Task ReorderAsync(ReorderRequest request)
    {
        Check(state.Revision, request.Revision);
        if (request.Ids.Length != state.Documents.Count || request.Ids.Distinct().Count() != state.Documents.Count
            || request.Ids.Any(id => !state.Documents.ContainsKey(id))) throw new WorkspaceException(400, "The document list changed. Refresh and try again.");
        var candidate = state.Manifest.Clone();
        for (var i = 0; i < request.Ids.Length; i++) candidate.Documents[request.Ids[i]].Order = i;
        await state.CommitManifestAsync(candidate, state.Revision);
        state.PublishChanges();
    }
}
