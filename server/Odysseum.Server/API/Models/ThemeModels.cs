namespace Odysseum.Server.API.Models;

/// <summary>A colour scheme the interface may save: one Tailwind palette per role.</summary>
public record ThemeRequest(Dictionary<string, string> Colors);
public record ThemeResponse(string Name, Dictionary<string, string> Colors);
/// <summary>The roles a theme colours and the palettes they may name, so the interface never guesses.</summary>
public record ThemeOptionsResponse(IEnumerable<string> Roles, IEnumerable<string> Palettes);
