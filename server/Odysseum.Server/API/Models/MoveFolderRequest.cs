using System.ComponentModel.DataAnnotations;

namespace Odysseum.Server.API.Models;

/// <summary>Puts the folder at <c>Index</c> among the children of the folder <c>TargetFolderId</c>. The index is clamped.</summary>
public record MoveFolderRequest(string TargetFolderId, [Required] int? Index);
