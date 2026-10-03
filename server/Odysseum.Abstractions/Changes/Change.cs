namespace Odysseum.Abstractions.Changes;

/// <summary>One item that a write or a read of a project changed. <c>ETag</c> is null for a removed item.</summary>
public sealed record Change(ChangeKind Kind, ItemType Type, string ProjectId, string Id, string? ETag);
