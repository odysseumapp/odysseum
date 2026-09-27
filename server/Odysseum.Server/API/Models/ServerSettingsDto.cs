namespace Odysseum.Server.API.Models;

/// <summary>The server settings the browser may read and change. Everything else stays in the settings file or environment.</summary>
public record ServerSettingsDto(bool AllowDeletingDefaultFolders);
