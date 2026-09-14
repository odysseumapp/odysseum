using System.Security.Cryptography;

namespace Odysseum.Server.Services.Storage;

internal static class ContentRevision
{
    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static void Check(string actual, string expected)
    {
        if (string.IsNullOrEmpty(expected) || actual != expected)
            throw new WorkspaceException(409, "This document or its metadata changed elsewhere. Compare the versions before saving.");
    }
}
