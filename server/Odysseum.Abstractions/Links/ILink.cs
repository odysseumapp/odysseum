using Odysseum.Abstractions.Items;

namespace Odysseum.Abstractions.Links;

/// <summary>A link between two documents of the same project. A link has no direction.</summary>
public interface ILink : IProjectItem
{
    string FirstDocumentId { get; }
    string SecondDocumentId { get; }
    /// <summary>Empty when there is no note.</summary>
    string Note { get; }
}
