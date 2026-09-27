using Odysseum.Abstractions.Projects;

namespace Odysseum.Abstractions.Documents.Events;

public sealed class DocumentEventArgs(ProjectBranch branch, IDocument document) : EventArgs
{
    public ProjectBranch Branch { get; } = branch;
    public IDocument Document { get; } = document;
}
