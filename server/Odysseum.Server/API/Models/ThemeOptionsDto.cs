namespace Odysseum.Server.API.Models;

/// <summary>The roles a theme colours and the palettes they may name.</summary>
public record ThemeOptionsDto(IEnumerable<string> Roles, IEnumerable<string> Palettes);
