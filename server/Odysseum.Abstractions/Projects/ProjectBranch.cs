namespace Odysseum.Abstractions.Projects;

/// <summary>Names one branch of one project. <c>Project</c> is the project's folder name in the workspace.
/// Only the <c>main</c> branch exists today.</summary>
public sealed record ProjectBranch(string Project, string Branch)
{
    public const string MainBranch = "main";

    public static ProjectBranch Main(string project) => new(project, MainBranch);

    public bool IsMain => Branch == MainBranch;

    public override string ToString() => Project + "@" + Branch;
}
