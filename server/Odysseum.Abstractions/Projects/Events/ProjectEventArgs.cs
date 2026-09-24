namespace Odysseum.Abstractions.Projects.Events;

public sealed class ProjectEventArgs(IProject project) : EventArgs
{
    public IProject Project { get; } = project;
}
