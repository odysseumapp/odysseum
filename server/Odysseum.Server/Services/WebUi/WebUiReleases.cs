using System.Security.Cryptography;
using System.Text.Json;

namespace Odysseum.Server.Services.WebUi;

/// <summary>Release discovery uses a static JSON feed, never the GitHub API.</summary>
public sealed class WebUiReleases(HttpClient http, string feedUrl = WebUiReleases.DefaultFeed)
{
    public const string DefaultFeed = "https://raw.githubusercontent.com/odysseumapp/odysseum-web/releases/releases.json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<WebUiReleaseEntry> DownloadAsync(string destination, string version = "latest")
    {
        using var response = await http.GetAsync(Https(feedUrl), HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(1024 * 1024);
        var feed = JsonSerializer.Deserialize<WebUiReleaseFeed>(await response.Content.ReadAsStringAsync(), Json);
        if (feed is null || feed.SchemaVersion != 1 || feed.Releases is null)
            throw new InvalidDataException("The UI release feed uses an unsupported format.");
        var release = feed.Releases.FirstOrDefault(item => item.ApiVersion == WebUiRelease.SupportedApiVersion
            && (version == "latest" || item.Version == version))
            ?? throw new InvalidDataException($"No compatible UI release '{version}' is listed in the release feed.");
        if (string.IsNullOrWhiteSpace(release.Version) || release.Sha256 is null || release.Sha256.Length != 64
            || release.Sha256.Any(c => !char.IsAsciiHexDigit(c))) throw new InvalidDataException("The UI release entry is invalid.");
        using var download = await http.GetAsync(Https(release.Url), HttpCompletionOption.ResponseHeadersRead);
        download.EnsureSuccessStatusCode();
        if (download.Content.Headers.ContentLength > WebUiInstallation.MaxArchiveBytes)
            throw new InvalidDataException("The UI archive exceeds 100 MB.");
        destination = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await using (var source = await download.Content.ReadAsStreamAsync())
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer)) > 0)
                {
                    total += read;
                    if (total > WebUiInstallation.MaxArchiveBytes) throw new InvalidDataException("The UI archive exceeds 100 MB.");
                    await target.WriteAsync(buffer.AsMemory(0, read));
                }
            }
            await using (var file = File.OpenRead(temporary))
                if (!Convert.ToHexStringLower(await SHA256.HashDataAsync(file)).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The UI archive checksum does not match the release feed.");
            File.Move(temporary, destination, overwrite: true);
            return release;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static Uri Https(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        ? uri : throw new InvalidDataException("UI release feeds and downloads must use HTTPS.");
}
