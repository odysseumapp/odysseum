using Odysseum.Server.API.Models;
using Odysseum.Server.Settings;

namespace Odysseum.Server.API.Middleware;

/// <summary>When a password is configured, every API route except session and login requires the cookie.</summary>
public class WorkspacePasswordMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ISettingsProvider _settingsProvider;

    public WorkspacePasswordMiddleware(RequestDelegate next, ISettingsProvider settingsProvider)
    {
        _next = next;
        _settingsProvider = settingsProvider;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var settings = _settingsProvider.GetSettings();
        if (settings.PasswordRequired && context.Request.Path.StartsWithSegments("/api")
            && context.Request.Path != "/api/session" && context.Request.Path != "/api/login"
            && context.User.Identity?.IsAuthenticated != true)
        {
            await ApiResults.Unauthorized("Unlock this workspace to continue.").ExecuteAsync(context);
            return;
        }
        await _next(context);
    }
}
