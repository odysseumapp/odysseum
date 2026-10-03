using System.Text.Json;
using System.Text.RegularExpressions;
using Odysseum.Abstractions.Exceptions;

namespace Odysseum.Server.Services.Views;

/// <summary>The only rules the server has for views: the form of a view name, and that settings are a JSON object of
/// limited size. Which views exist is up to the plugins and their clients.</summary>
public static partial class ViewNames
{
    public const int MaxSettingsBytes = 64 * 1024;

    /// <summary>Lowercase letters, digits, '.', '-' and '_', starting with a letter or digit, at most 64 characters.</summary>
    public static bool IsValid(string? name) => name is not null && Pattern().IsMatch(name);

    public static void Check(string? name)
    {
        if (!IsValid(name)) throw new WorkspaceException(WorkspaceError.Invalid,
            "A view name uses lowercase letters, digits, '.', '-' and '_', and is at most 64 characters.");
    }

    /// <summary>True for a JSON null, which removes a view's settings.</summary>
    public static bool Removes(JsonElement settings) => settings.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;

    public static void CheckSettings(IReadOnlyDictionary<string, JsonElement> views)
    {
        foreach (var (name, settings) in views)
        {
            Check(name);
            if (settings.ValueKind != JsonValueKind.Object)
                throw new WorkspaceException(WorkspaceError.Invalid, $"The settings of the '{name}' view must be a JSON object.");
        }
        if (JsonSerializer.SerializeToUtf8Bytes(views).Length > MaxSettingsBytes)
            throw new WorkspaceException(WorkspaceError.TooLarge, "A folder's view settings exceed 64 KB.");
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$")] private static partial Regex Pattern();
}
