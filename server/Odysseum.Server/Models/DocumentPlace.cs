namespace Odysseum.Server.Models;

/// <summary>Where a document is stored. <c>Path</c> is relative to the project, for example <c>Manuscript/Chapter 1.md</c>.
/// A change to the path moves or renames the document's file.</summary>
public sealed record DocumentPlace(string DocumentId, string ProjectId, string Path, string ETag);
