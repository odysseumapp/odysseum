using Odysseum.Server.Models;

namespace Odysseum.Server.Services;

/// <summary>Puts documents in the order of the folder tree. A folder's <c>ChildIds</c> is the only source of the order.</summary>
internal static class TreeOrder
{
    /// <summary>Every document under the folder: a folder's own document first, then its children in order, with each
    /// subfolder's documents in its place.</summary>
    public static IReadOnlyList<Document> Documents(Folder root, IEnumerable<Folder> folders, IEnumerable<Document> documents)
    {
        var foldersById = folders.ToDictionary(folder => folder.Id, StringComparer.Ordinal);
        var documentsById = documents.ToDictionary(document => document.Id, StringComparer.Ordinal);
        var result = new List<Document>();
        void Add(Folder folder)
        {
            if (folder.OwnDocumentId is { } own && documentsById.TryGetValue(own, out var ownDocument)) result.Add(ownDocument);
            foreach (var id in folder.ChildIds)
            {
                if (documentsById.TryGetValue(id, out var document)) result.Add(document);
                else if (foldersById.TryGetValue(id, out var subfolder)) Add(subfolder);
            }
        }
        Add(root);
        return result;
    }

    /// <summary>The documents among the folder's children, in order, without the folder's own document.</summary>
    public static IReadOnlyList<Document> Children(Folder folder, IEnumerable<Document> documents)
    {
        var documentsById = documents.ToDictionary(document => document.Id, StringComparer.Ordinal);
        return folder.ChildIds.Select(id => documentsById.GetValueOrDefault(id)).OfType<Document>().ToArray();
    }
}
