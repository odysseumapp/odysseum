namespace Odysseum.Server.API.Enums;

/// <summary>
/// What a document is, derived from its top-level folder. Characters, Locations, Threads, and the note
/// folders hold supporting documents; other folders hold manuscript scenes. All kinds share the same file format.
/// </summary>
public enum DocumentKind
{
    Scene,
    Note,
    Character,
    Location,
    Thread,
}
