namespace Odysseum.Server.API.Models;

public record SessionDto(bool Authenticated, bool PasswordRequired, bool AllowDeletingDefaultFolders);
