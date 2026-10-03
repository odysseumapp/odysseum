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

    /// <summary>The folder with one subfolder per plugin. The default is <c>plugins</c> next to the server.</summary>
    string? Plugins { get; set; }

    /// <summary>Ids (from each plugin.json) of the plugins that are not loaded.</summary>
    List<string> DisabledPlugins { get; set; }

    bool AllowDeletingDefaultFolders { get; set; }

    int ScanSeconds { get; set; }

    int VersionSeconds { get; set; }

    bool PasswordRequired { get; }
}
