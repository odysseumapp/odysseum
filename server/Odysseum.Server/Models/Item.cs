using Odysseum.Abstractions.Items;

namespace Odysseum.Server.Models;

/// <summary>A folder or a document. Items do not change; every edit makes a copy. <see cref="ParentId"/>,
/// <see cref="OrderInParent"/> and <see cref="Path"/> are set only by <see cref="Project"/> when it builds the tree.
/// An item that is not placed yet has no parent, order -1 and its name as path.</summary>
public abstract class Item : IItem
{
    protected Item(string id, string name, string? parentId, int orderInParent, string path)
    {
        Id = id;
        Name = name;
        ParentId = parentId;
        OrderInParent = orderInParent;
        Path = path;
    }

    public string Id { get; }
    public string Name { get; }
    public string? ParentId { get; }
    public int OrderInParent { get; }
    /// <summary>The project-relative path. The root folder has the empty path.</summary>
    public string Path { get; }

    internal static string Join(string parentPath, string name) => parentPath.Length == 0 ? name : parentPath + "/" + name;

    internal static string ParentPath(string path) => System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "";
}
