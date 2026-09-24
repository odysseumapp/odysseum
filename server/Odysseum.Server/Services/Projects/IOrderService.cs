using Odysseum.Abstractions.Documents;
using Odysseum.Abstractions.Folders;
using Odysseum.Abstractions.Projects;

namespace Odysseum.Server.Services;

public sealed record FolderChild(IFolder? Folder, IDocument? Document);

public interface IOrderService
{
    Task<IReadOnlyList<FolderChild>> ChildrenAsync(IFolder folder);
    Task<IReadOnlyList<IFolder>> FoldersAsync(IFolder folder);
    Task<IReadOnlyList<IDocument>> DocumentsAsync(IProject project);
    Task ArrangeChildrenAsync(IFolder folder, IReadOnlyList<string> orderedIds, string expectedRevision);
}
