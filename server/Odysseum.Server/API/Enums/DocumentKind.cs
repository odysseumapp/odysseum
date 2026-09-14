namespace Odysseum.Server.API.Enums;

/// <summary>
/// What a document is, derived from its top-level folder: <c>Characters/</c> and <c>Locations/</c> hold story references, the note folders
/// hold story notes, everything else is manuscript. All kinds share the same file format and metadata.
/// </summary>
public enum DocumentKind
{
    Scene,
    Note,
    Character,
    Location,
    Arc,
}
