namespace Odysseum.Abstractions.Documents;

public interface IDocumentVersion
{
    string Id { get; }
    IDocument Document { get; }
    DateTimeOffset Created { get; }
    Task<string> ReadBodyAsync();
}
