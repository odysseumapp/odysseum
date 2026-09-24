namespace Odysseum.Server.Repositories.Manifests;

internal sealed record DiskDocument(string Id, string Path, byte[] Bytes, string Prefix,
    string Body, string Revision, DateTime Modified);
