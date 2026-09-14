namespace Odysseum.Server.Services.Storage.Models;

/// <summary>A document as last read from disk. Prefix preserves frontmatter/BOM; Body holds editable prose.</summary>
internal sealed record DiskDocument(string Id, string Path, byte[] Bytes, string Prefix,
    string Body, string Revision, DateTime Modified);
