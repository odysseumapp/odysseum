namespace Odysseum.Abstractions.Items;

/// <summary>An item that belongs to one project: a folder, a document or a link.</summary>
public interface IProjectItem : IItem
{
    string ProjectId { get; }
}
