using System.Security.Cryptography;
using System.Text;
using Odysseum.Abstractions.Exceptions;

namespace Odysseum.Server.Repositories;

/// <summary>Makes and compares ETags. An ETag is a SHA-256 hash of the stored values of one item.</summary>
public static class ETags
{
    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static string FromValues(params object?[] values) =>
        Hash(Encoding.UTF8.GetBytes(string.Join('\n', values.Select(value => value?.ToString() ?? ""))));

    /// <summary>Throws <see cref="WorkspaceError.ETagMismatch"/> when the ETag the caller gave is not the current one.</summary>
    public static void Check(string current, string? expected)
    {
        if (string.IsNullOrEmpty(expected) || current != expected)
            throw new WorkspaceException(WorkspaceError.ETagMismatch, "This item changed after you read it. Read it again before you save.");
    }
}
