using System.ComponentModel.DataAnnotations;

namespace Odysseum.Server.API.Models;

public record UpdateServerSettingsRequest([Required] bool? AllowDeletingDefaultFolders);
