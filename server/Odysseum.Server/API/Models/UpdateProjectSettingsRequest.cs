using System.ComponentModel.DataAnnotations;

namespace Odysseum.Server.API.Models;

/// <summary>All settings of the project. The server replaces all of them.</summary>
public record UpdateProjectSettingsRequest(string Title, [Required] int? WordGoal, [Required] int? DefaultSceneWordGoal);
