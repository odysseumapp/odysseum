using Odysseum.Server.Models;

namespace Odysseum.Server.Repositories;

/// <summary>Where each document is stored. Adding a place makes an empty file; updating it moves or renames the file;
/// deleting it deletes the file.</summary>
public interface IDocumentPlaceRepository : IRepository<DocumentPlace>
{
    /// <summary>The document's path relative to the project. Throws when the document does not exist.</summary>
    Task<string> GetPathByDocumentIdAsync(string documentId);
    /// <summary>The document's place. Throws when the document does not exist.</summary>
    Task<DocumentPlace> GetPlaceByDocumentIdAsync(string documentId);
    /// <summary>The places of the documents directly in the folder at that path, with the folder's own document.</summary>
    Task<IReadOnlyList<DocumentPlace>> GetPlacesInFolderAsync(string projectId, string folderPath);
}
