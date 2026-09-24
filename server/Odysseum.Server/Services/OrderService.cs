using Odysseum.Abstractions.Documents;
using Odysseum.Abstractions.Exceptions;
using Odysseum.Abstractions.Folders;
using Odysseum.Abstractions.Projects;
using Odysseum.Server.Models;
using Odysseum.Server.Repositories;
using Odysseum.Server.Repositories.Manifests;

namespace Odysseum.Server.Services;

public sealed class OrderService : IOrderService
{
    public Task<IReadOnlyList<FolderChild>> ChildrenAsync(IFolder folder) => Task.FromResult(Children(Model(folder)));

    public Task<IReadOnlyList<IFolder>> FoldersAsync(IFolder folder) =>
        Task.FromResult<IReadOnlyList<IFolder>>(Children(Model(folder)).Where(child => child.Folder is not null).Select(child => child.Folder!).ToArray());

    public Task<IReadOnlyList<IDocument>> DocumentsAsync(IProject project) =>
        Task.FromResult<IReadOnlyList<IDocument>>(Walk(Model(project).RootFolder).ToArray());

    public Task ArrangeChildrenAsync(IFolder folder, IReadOnlyList<string> orderedIds, string expectedRevision) =>
        ArrangeAsync(folder.Project, new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { [folder.Id] = orderedIds }, expectedRevision);

    public Task ArrangeAsync(IProject project, IReadOnlyDictionary<string, IReadOnlyList<string>> childrenByFolder, string expectedRevision)
    {
        var open = OpenProject.Of(project);
        return open.RunAsync(async () =>
        {
            if (expectedRevision != open.Current.Revision)
                throw new WorkspaceException(WorkspaceError.Conflict, "The project changed. Refresh before saving again.");
            var folders = childrenByFolder.Select(pair => Arranged(open.Current, pair.Key, pair.Value)).ToArray();
            await open.Folders.SaveAsync(folders);
            return true;
        });
    }

    public Task ArrangeDocumentsAsync(IProject project, IReadOnlyList<string> orderedIds, string expectedRevision)
    {
        var open = OpenProject.Of(project);
        return open.RunAsync(async () =>
        {
            var current = open.Current;
            ContentRevision.Check(current.Revision, expectedRevision);
            var required = current.Documents.Where(d => !d.IsFolderDocument).Select(d => d.Id);
            if (orderedIds.Distinct().Count() != orderedIds.Count || orderedIds.Any(id => current.Document(id) is null) || required.Any(id => !orderedIds.Contains(id)))
                throw new WorkspaceException(WorkspaceError.Invalid, "The document list changed. Refresh and try again.");
            var position = orderedIds.Select((id, index) => (id, index)).ToDictionary(pair => pair.id, pair => pair.index, StringComparer.Ordinal);
            var folders = current.Folders.Select(folder => folder.WithItemOrder(Children(folder)
                .Select((child, index) => (child, index))
                .OrderBy(pair => Rank(pair.child, position)).ThenBy(pair => pair.index)
                .Select(pair => pair.child.Folder?.Id ?? pair.child.Document!.Id).ToArray())).ToArray();
            await open.Folders.SaveAsync(folders);
            return true;
        });
    }

    private static int Rank(FolderChild child, Dictionary<string, int> position)
    {
        if (child.Document is { } document) return position.GetValueOrDefault(document.Id, int.MaxValue);
        var positions = Walk((Folder)child.Folder!).Where(d => position.ContainsKey(d.Id)).Select(d => position[d.Id]).ToArray();
        return positions.Length == 0 ? int.MaxValue : positions.Min();
    }

    private static Folder Arranged(Project project, string folderId, IReadOnlyList<string> orderedIds)
    {
        var folder = project.Folder(folderId) ?? throw new WorkspaceException(WorkspaceError.NotFound, "The folder no longer exists.");
        var keys = folder.ChildFolders.Select(f => f.Id).Concat(folder.ChildDocuments.Where(d => !d.IsFolderDocument).Select(d => d.Id)).ToHashSet(StringComparer.Ordinal);
        if (orderedIds.Distinct().Count() != orderedIds.Count || orderedIds.Any(id => !keys.Contains(id)))
            throw new WorkspaceException(WorkspaceError.Invalid, "Layouts must refer to immediate children.");
        return folder.WithItemOrder([.. orderedIds]);
    }

    internal static IReadOnlyList<FolderChild> Children(Folder folder)
    {
        var folders = folder.ChildFolders.ToDictionary(f => f.Id, StringComparer.Ordinal);
        var documents = folder.ChildDocuments.Where(d => !d.IsFolderDocument).ToDictionary(d => d.Id, StringComparer.Ordinal);
        var result = new List<FolderChild>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in folder.ItemOrder)
        {
            if (!seen.Add(id)) continue;
            if (folders.TryGetValue(id, out var child)) result.Add(new(child, null));
            else if (documents.TryGetValue(id, out var document)) result.Add(new(null, document));
        }
        result.AddRange(folders.Values.Where(f => !seen.Contains(f.Id))
            .OrderBy(f => folder.IsRoot ? DefaultRank(f.Name) : 0).ThenBy(f => f.Name, StringComparer.Ordinal)
            .Select(f => new FolderChild(f, null)));
        result.AddRange(documents.Values.Where(d => !seen.Contains(d.Id))
            .OrderBy(d => d.Path, StringComparer.Ordinal)
            .Select(d => new FolderChild(null, d)));
        return result;
    }

    internal static IEnumerable<Document> Walk(Folder folder)
    {
        if (folder.OwnDocument is { } own) yield return own;
        foreach (var child in Children(folder))
        {
            if (child.Document is Document document) yield return document;
            else foreach (var nested in Walk((Folder)child.Folder!)) yield return nested;
        }
    }

    internal static void Append(FolderManifest owner, string id)
    {
        if (!owner.ItemOrder.Contains(id, StringComparer.Ordinal)) owner.ItemOrder = [.. owner.ItemOrder, id];
    }

    internal static void Remove(FolderManifest owner, string id) =>
        owner.ItemOrder = owner.ItemOrder.Where(key => key != id).ToArray();

    private static int DefaultRank(string name)
    {
        var index = Array.FindIndex(ProjectLibrary.DefaultFolders, folder => string.Equals(folder, name, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? int.MaxValue : index;
    }

    private static Folder Model(IFolder folder) => folder as Folder
        ?? throw new WorkspaceException(WorkspaceError.Invalid, "That folder is not open in this workspace.");

    private static Project Model(IProject project) => project as Project
        ?? throw new WorkspaceException(WorkspaceError.Invalid, "That project is not open in this workspace.");
}
