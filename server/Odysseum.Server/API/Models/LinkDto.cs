using Odysseum.Abstractions.Links;
using System.Text.Json.Serialization;

namespace Odysseum.Server.API.Models;

public record LinkDto(string Id, string ProjectId, string FirstDocumentId, string SecondDocumentId, string Note, [property: JsonPropertyName("etag")] string ETag)
{
    public static LinkDto FromLink(ILink link) =>
        new(link.Id, link.ProjectId, link.FirstDocumentId, link.SecondDocumentId, link.Note, link.ETag);
}
