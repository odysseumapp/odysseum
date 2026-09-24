namespace Odysseum.Server.Settings;

public interface IServerSettings
{
    string Workspace { get; set; }

    string? Password { get; set; }

    bool Demo { get; set; }

    string? Keys { get; set; }

    string? WebUi { get; set; }

    string? Themes { get; set; }

    string? Templates { get; set; }

    bool AllowDeletingDefaultFolders { get; set; }

    int ScanSeconds { get; set; }

    int VersionSeconds { get; set; }

    bool PasswordRequired { get; }
}
