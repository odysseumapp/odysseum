namespace Odysseum.Server.API.Filters;

public static class ETagHeader
{
    /// <summary>The ETag in an If-Match header value, without the quotes and without a "W/" prefix.</summary>
    public static string Parse(string? ifMatch)
    {
        var value = (ifMatch ?? "").Trim();
        if (value.StartsWith("W/", StringComparison.Ordinal)) value = value[2..];
        return value.Trim('"');
    }
}
