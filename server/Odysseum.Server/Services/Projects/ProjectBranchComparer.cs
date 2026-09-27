using Odysseum.Abstractions.Projects;

namespace Odysseum.Server.Services.Projects;

/// <summary>Compares project names the way the file system does: without case on Windows, exactly elsewhere.
/// Branch names always compare exactly.</summary>
public sealed class ProjectBranchComparer : IEqualityComparer<ProjectBranch>
{
    public static readonly ProjectBranchComparer Instance = new();
    private static readonly StringComparer Names = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public bool Equals(ProjectBranch? x, ProjectBranch? y) =>
        x is null ? y is null : y is not null && Names.Equals(x.Project, y.Project) && string.Equals(x.Branch, y.Branch, StringComparison.Ordinal);

    public int GetHashCode(ProjectBranch obj) => HashCode.Combine(Names.GetHashCode(obj.Project), obj.Branch);
}
