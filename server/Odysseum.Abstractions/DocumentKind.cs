namespace Odysseum.Abstractions;

/// <summary>What a document is for. The store decides the kind from where the document lives; callers never derive it themselves.</summary>
public enum DocumentKind
{
    Scene,
    Note,
    Character,
    Location,
    Thread,
    Style,
}
