namespace Odysseum.Server.API.Models;

/// <summary>A colour scheme to save, with one Tailwind palette for each role.</summary>
public record SaveThemeRequest(Dictionary<string, string> Colors);
