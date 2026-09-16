namespace Odysseum.Server.API.Models;

/// <summary>The server settings the interface may change. Everything else stays in the settings file or environment.</summary>
public record ServerSettingsRequest(bool AllowDeletingDefaultFolders);
public record ServerSettingsResponse(bool AllowDeletingDefaultFolders);
