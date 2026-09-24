using Odysseum.Abstractions.Documents;
using Odysseum.Abstractions.Projects;

namespace Odysseum.Abstractions.Folders;

public interface IFolder
{
    string Id { get; }
    IProject Project { get; }
    IFolder? Parent { get; }
    string Name { get; }
    FolderView? PinnedView { get; }
    IFolder? GridFolder { get; }
    IReadOnlyList<IFolder> Folders { get; }
    IReadOnlyList<IDocument> Documents { get; }
}
