namespace Odysseum.Abstractions;

/// <summary>One recovery snapshot of a document's body. <c>Id</c> is opaque; pass it back to read the snapshot.</summary>
public sealed record HistoryEntry(string Id, DateTimeOffset Created);
