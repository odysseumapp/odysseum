namespace Odysseum.Server.Services.WebUi;

public sealed record WebUiRelease(string Version, int ApiVersion, string BasePath)
{
    public const int SupportedApiVersion = 1;
    public const string ManifestName = "webui-release.json";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Version) || Version.Length > 100 || ApiVersion != SupportedApiVersion || BasePath != "/webui/")
            throw new InvalidDataException("This UI release is not compatible with this server (API version 1, base path /webui/ required).");
    }
}

public sealed record WebUiReleaseEntry(string Version, string Url, string Sha256, int ApiVersion);
public sealed record WebUiReleaseFeed(int SchemaVersion, WebUiReleaseEntry[] Releases);
