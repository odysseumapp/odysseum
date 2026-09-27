using Odysseum.Abstractions.Documents;
using System.Text.Json.Serialization;

namespace Odysseum.Server.API.Models;

/// <summary>A document's details, without its text.</summary>
public record DocumentDto(string Id, string ProjectId, string FolderId, string Name, DocumentKind Kind, bool IsFolderDocument,
    string Title, string Synopsis, string Notes, DocumentStatus Status, int WordGoal, int WordCount, DateTimeOffset LastModified, [property: JsonPropertyName("etag")] string ETag)
{
    public static DocumentDto FromDocument(IDocument document) => new(document.Id, document.ProjectId, document.FolderId, document.Name,
        document.Kind, document.IsFolderDocument, document.Title, document.Synopsis, document.Notes, document.Status, document.WordGoal,
        document.WordCount, document.LastModified, document.ETag);
}
