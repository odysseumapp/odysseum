namespace Odysseum.Server.API.Models;

public record LoginRequest(string Password);

public record SessionResponse(bool Authenticated, bool PasswordRequired, bool AllowDeletingDefaultFolders);
