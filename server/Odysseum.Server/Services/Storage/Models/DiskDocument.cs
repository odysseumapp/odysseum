namespace Odysseum.Server.Services.Storage.Models;

internal sealed record DiskDocument(string Id, string Path, byte[] Bytes, string Prefix,
    string Body, string Revision, DateTime Modified);
