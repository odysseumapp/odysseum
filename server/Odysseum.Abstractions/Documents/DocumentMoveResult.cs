using Odysseum.Abstractions.Folders;

namespace Odysseum.Abstractions.Documents;

/// <summary>A moved document, with the folder it left and the folder it went to. The two are the same folder when the
/// move only changed the order.</summary>
public sealed record DocumentMoveResult(IDocument Document, IFolder OldFolder, IFolder NewFolder);
