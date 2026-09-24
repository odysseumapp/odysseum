namespace Odysseum.Abstractions;

public readonly record struct FolderId(string Value)
{
    public static FolderId New() => new(Guid.NewGuid().ToString("D"));
    public static bool TryParse(string? value, out FolderId id)
    {
        id = default;
        if (!Guid.TryParseExact(value, "D", out _)) return false;
        id = new FolderId(value!);
        return true;
    }
    public override string ToString() => Value;
}
