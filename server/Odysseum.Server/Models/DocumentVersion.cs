using Odysseum.Abstractions.Documents;

namespace Odysseum.Server.Models;

public sealed class DocumentVersion(IDocument document, string id, DateTimeOffset created, Func<Task<string>> read) : IDocumentVersion
{
    public string Id { get; } = id;
    public IDocument Document { get; } = document;
    public DateTimeOffset Created { get; } = created;
    public Task<string> ReadBodyAsync() => read();
}
