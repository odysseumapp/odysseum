using System.ComponentModel.DataAnnotations;
using Odysseum.Abstractions.Documents;

namespace Odysseum.Server.API.Models;

/// <summary>All details of the document. The server replaces all of them.</summary>
public record UpdateDocumentDetailsRequest(string Title, string Synopsis, string Notes, [Required] DocumentStatus? Status, [Required] int? WordGoal);
