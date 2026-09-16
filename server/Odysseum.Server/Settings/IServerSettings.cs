namespace Odysseum.Server.Settings;

public interface IServerSettings
{
    /// <summary>Root directory holding one folder per project.</summary>
    string Workspace { get; set; }

    /// <summary>Optional workspace password. Environment only; never persisted.</summary>
    string? Password { get; set; }

    /// <summary>Seed a completely empty workspace with the sample project.</summary>
    bool Demo { get; set; }

    /// <summary>Directory for persistent data-protection keys; null uses the ASP.NET Core default.</summary>
    string? Keys { get; set; }

    /// <summary>Installed, independently versioned static UI files. Null selects webui beside the settings file.</summary>
    string? WebUi { get; set; }

    /// <summary>Whether the folders every project starts with may be removed once empty.</summary>
    bool AllowDeletingDefaultFolders { get; set; }

    /// <summary>Full reconciliation interval for every open project.</summary>
    int ScanSeconds { get; set; }

    bool PasswordRequired { get; }
}
