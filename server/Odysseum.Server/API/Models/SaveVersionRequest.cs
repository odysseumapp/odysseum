namespace Odysseum.Server.API.Models;

/// <summary>Saves the project as it is now under a name. The server also saves automatic versions on its own.</summary>
public record SaveVersionRequest(string Name);
