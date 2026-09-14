using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Odysseum.Server.API.Models;
using Odysseum.Server.Settings;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Odysseum.Server.API.Controllers;

[ApiController]
[Route("api")]
public class SessionController : ControllerBase
{
    private readonly ISettingsProvider _settingsProvider;

    public SessionController(ISettingsProvider settingsProvider)
    {
        _settingsProvider = settingsProvider;
    }

    /// <summary>Whether the workspace is unlocked for this browser.</summary>
    [HttpGet("session")]
    public IResult GetSession()
    {
        var settings = _settingsProvider.GetSettings();
        var authenticated = !settings.PasswordRequired || User.Identity?.IsAuthenticated == true;
        return ApiResults.Success(new SessionResponse(authenticated, settings.PasswordRequired));
    }

    /// <summary>Unlock the workspace with the configured password.</summary>
    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<IResult> Login([FromBody] LoginRequest request)
    {
        var settings = _settingsProvider.GetSettings();
        if (settings.PasswordRequired && !CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(request.Password ?? "")), SHA256.HashData(Encoding.UTF8.GetBytes(settings.Password!))))
        {
            return ApiResults.Unauthorized("That password didn't match. Try again.");
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "Writer")], CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return ApiResults.Success(new SessionResponse(true, settings.PasswordRequired));
    }

    /// <summary>Lock the workspace for this browser.</summary>
    [HttpPost("logout")]
    public async Task<IResult> Logout()
    {
        var settings = _settingsProvider.GetSettings();
        await HttpContext.SignOutAsync();
        return ApiResults.Success(new SessionResponse(!settings.PasswordRequired, settings.PasswordRequired));
    }
}
