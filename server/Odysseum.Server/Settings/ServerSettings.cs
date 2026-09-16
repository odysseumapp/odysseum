using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Odysseum.Server.Settings;

public class ServerSettings : IServerSettings
{
    [Required(ErrorMessage = "A workspace directory is required.")]
    public string Workspace { get; set; } = "workspace";

    [JsonIgnore]
    public string? Password { get; set; }

    public bool Demo { get; set; }

    public string? Keys { get; set; }
    public string? WebUi { get; set; }

    public bool AllowDeletingDefaultFolders { get; set; }

    [Range(1, 300, ErrorMessage = "Scan interval must be between 1 and 300 seconds.")]
    public int ScanSeconds { get; set; } = 3;

    [JsonIgnore]
    public bool PasswordRequired => !string.IsNullOrEmpty(Password);
}
