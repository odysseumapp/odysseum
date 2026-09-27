namespace Odysseum.Server.API.Models;

/// <summary><c>TemplateName</c> names the project template to start from. Without it, the project starts from <c>Default</c>.</summary>
public record CreateProjectRequest(string Title, int? WordGoal, string? TemplateName);
