namespace Odysseum.Abstractions.Items;

/// <summary>An item as it was at one moment. It does not change; a later read gives a new one.</summary>
public interface IItem
{
    string Id { get; }
    /// <summary>Changes when the stored item changes. A change request must give the ETag it last saw.</summary>
    string ETag { get; }
}
