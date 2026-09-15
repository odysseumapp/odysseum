using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Odysseum.Server.API.Middleware;
using Odysseum.Server.API.Models;
using Odysseum.Server.Bootstrap;
using Odysseum.Server.Services;
using Odysseum.Server.Settings;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

var startupLoggers = LoggerFactory.Create(logging => logging.AddConsole());

var settingsProvider = new SettingsProvider(startupLoggers.CreateLogger<SettingsProvider>(), builder.Configuration, builder.Environment);
var settings = settingsProvider.GetSettings();

settingsProvider.DebugSettingsToLog();

builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = 5 * 1024 * 1024);

builder.Services.AddSingleton<ISettingsProvider>(settingsProvider);

builder.Services.AddSingleton(provider => new ProjectFactory(provider.GetRequiredService<ILoggerFactory>(), settings.ScanSeconds));
builder.Services.AddSingleton(provider => new ProjectLibrary(settings.Workspace, provider.GetRequiredService<ProjectFactory>()));
// ApiResults writes through TypedResults.Json, which uses these options; MVC's AddJsonOptions below only covers model binding.

builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

builder.Services.AddControllers()
    .AddJsonOptions(json => json.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)))
    .ConfigureApiBehaviorOptions(behavior => behavior.InvalidModelStateResponseFactory = context =>
    {
        // JSON binding failures are keyed "$.field"; name the field rather than echoing the generic "request is required".
        var field = context.ModelState.Keys.FirstOrDefault(key => key.StartsWith("$."))?[2..];
        var message = field is not null ? $"The value for '{field}' could not be read."
            : context.ModelState.Values.SelectMany(state => state.Errors).Select(error => error.ErrorMessage)
                .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? "The request could not be read.";
        return ApiResults.ToActionResult(ApiResults.BadRequest(message));
    });

builder.Services.AddOpenApi();

if (!string.IsNullOrEmpty(settings.Keys))
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(settings.Keys)).SetApplicationName("Odysseum");

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(cookie =>
{
    cookie.Cookie.Name = "odysseum.session";
    cookie.Cookie.HttpOnly = true;
    cookie.Cookie.SameSite = SameSiteMode.Strict;
    cookie.ExpireTimeSpan = TimeSpan.FromDays(7);
    cookie.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
    cookie.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
});

builder.Services.AddRateLimiter(limiter =>
{
    limiter.OnRejected = async (context, token) =>
        await ApiResults.TooManyRequests("Too many attempts. Try again in a minute.").ExecuteAsync(context.HttpContext);
    limiter.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();

DemoContent.Seed(settings);

await app.Services.GetRequiredService<ProjectLibrary>().ListAsync(); // Fail at startup when the workspace root is unreadable.

app.UseMiddleware<ResponseHeadersMiddleware>();
app.UseMiddleware<ApiExceptionMiddleware>();

app.UseAuthentication();

app.UseRateLimiter();

app.UseMiddleware<WorkspacePasswordMiddleware>();

app.MapOpenApi();
app.MapScalarApiReference(scalar => scalar.WithTitle("Odysseum API"));

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).ExcludeFromDescription();

app.MapControllers();

app.Run();
