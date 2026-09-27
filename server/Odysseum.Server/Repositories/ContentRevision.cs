using Odysseum.Abstractions.Exceptions;
using System.Security.Cryptography;

namespace Odysseum.Server.Repositories;

internal static class ContentRevision
{
    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static void Check(string actual, string expected)
    {
        if (string.IsNullOrEmpty(expected) || actual != expected)
            throw new WorkspaceException(WorkspaceError.Conflict, "This document or its metadata changed elsewhere. Compare the versions before saving.");
    }
}
