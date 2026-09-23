namespace Odysseum.Abstractions;

/// <summary>A document's identity, stable across renames, moves, and storage backends. The value is a GUID in "D" format.</summary>
public readonly record struct DocumentId(string Value)
{
    public static DocumentId New() => new(Guid.NewGuid().ToString("D"));
    public static bool TryParse(string? value, out DocumentId id)
    {
        id = default;
        if (!Guid.TryParseExact(value, "D", out _)) return false;
        id = new DocumentId(value!);
        return true;
    }
    public override string ToString() => Value;
}
