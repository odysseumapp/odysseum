using Odysseum.Server.Models;

namespace Odysseum.Server.Repositories;

public interface IDocumentRepository : IRepository<Document>
{
    event EventHandler<RepositoryChangeEventArgs<Document>>? ItemAdded;
    event EventHandler<RepositoryChangeEventArgs<Document>>? ItemUpdated;
    event EventHandler<RepositoryChangeEventArgs<Document>>? ItemRemoved;

    Task<IReadOnlyList<Document>> GetDocumentsByProjectIdAsync(string projectId);
    /// <summary>Saves a new document's details and text. The document's place must exist.</summary>
    Task<Document> AddAsync(Document item, string text);
    Task<string> GetTextByDocumentIdAsync(string documentId);
    Task<Document> UpdateTextAsync(string documentId, string text, string expectedETag);
}
