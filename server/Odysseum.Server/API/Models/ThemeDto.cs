namespace Odysseum.Server.API.Models;

/// <summary>A saved colour scheme, with one Tailwind palette for each role.</summary>
public record ThemeDto(string Name, Dictionary<string, string> Colors);
