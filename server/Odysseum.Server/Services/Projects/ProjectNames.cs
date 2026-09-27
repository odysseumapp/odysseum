using Odysseum.Abstractions.Exceptions;

namespace Odysseum.Server.Services.Projects;

/// <summary>Rules for a project name, which is the project's folder name in the workspace.</summary>
public static class ProjectNames
{
    public static string Validate(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || name is "." or ".." || name.StartsWith('.')
            || name.EndsWith('.') || name.EndsWith(' ') || name.Contains('/') || name.Contains('\\')
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new WorkspaceException(WorkspaceError.Invalid, "That project name is not allowed.");
        return name;
    }
}
