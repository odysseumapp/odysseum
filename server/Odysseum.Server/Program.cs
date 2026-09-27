using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Odysseum.Server.API.Middleware;
using Odysseum.Server.API.Models;
using Odysseum.Server.Bootstrap;
using Odysseum.Server.Services;
using Odysseum.Server.Settings;
using Odysseum.Abstractions.Documents;
using Odysseum.Abstractions.Folders;
using Odysseum.Abstractions.History;
using Odysseum.Abstractions.Projects;
using Odysseum.Abstractions.Links;
using Odysseum.Server.API.SignalR;
using Odysseum.Server.Repositories;
using Odysseum.Server.Repositories.Disk;
using Odysseum.Server.Repositories.Git;
using Odysseum.Server.Services.Templates;
using Odysseum.Server.Services.Views;
using Odysseum.Server.Services.WebUi;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

var startupLoggers = LoggerFactory.Create(logging => logging.AddConsole());

var settingsProvider = new SettingsProvider(startupLoggers.CreateLogger<SettingsProvider>(), builder.Configuration, builder.Environment);
var settings = settingsProvider.GetSettings();

var webUi = new WebUiInstallation(settings.WebUi!);
if (builder.Configuration["install-webui"] is { } archive)
{
    var release = await webUi.InstallAsync(archive);
    Console.WriteLine($"Installed web UI {release.Version} in {webUi.Root}");
    return;
}
if (builder.Configuration["update-webui"] is { } version || builder.Configuration["download-webui"] is not null)
{
    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    var releases = new WebUiReleases(http, builder.Configuration["webui-feed"] ?? WebUiReleases.DefaultFeed);
    var destination = builder.Configuration["download-webui"];
    var temporary = destination ?? Path.Combine(Path.GetTempPath(), "odysseum-webui-" + Guid.NewGuid().ToString("N") + ".zip");
    try
    {
        var release = await releases.DownloadAsync(temporary, builder.Configuration["update-webui"] ?? builder.Configuration["webui-version"] ?? "latest");
        if (destination is null) await webUi.InstallAsync(temporary, release.Sha256, release.Version);
        Console.WriteLine(destination is null ? $"Installed web UI {release.Version} in {webUi.Root}" : $"Downloaded web UI {release.Version} to {destination}");
    }
    finally { if (destination is null && File.Exists(temporary)) File.Delete(temporary); }
    return;
}
var bundledUi = Path.Combine(AppContext.BaseDirectory, "webui.zip");
if (webUi.CurrentDirectory is null && File.Exists(bundledUi)) await webUi.InstallAsync(bundledUi);
builder.Services.AddSingleton(webUi);
builder.Services.AddSingleton<WebUiFileProvider>();
builder.Services.AddSingleton<IThemeRepository>(new ThemeRepository(settings.Themes!));
var templates = new TemplateRepository(settings.Templates!);
try { templates.EnsureDefault(); }
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{ startupLoggers.CreateLogger<TemplateRepository>().LogWarning(ex, "Could not write the Default project template to {Path}; the built-in one is used", templates.Root); }
builder.Services.AddSingleton<ITemplateRepository>(templates);

settingsProvider.DebugSettingsToLog();

builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = 5 * 1024 * 1024);

builder.Services.AddSingleton<ISettingsProvider>(settingsProvider);

builder.Services.AddSingleton<OwnWrites>();
builder.Services.AddSingleton<IProjectWatcher>(provider => new FileProjectWatcher(settings.Workspace, provider.GetRequiredService<OwnWrites>(),
    settings.ScanSeconds, provider.GetRequiredService<ILogger<FileProjectWatcher>>()));
builder.Services.AddSingleton<IProjectHistory>(_ => new GitProjectHistory(settings.Workspace));
builder.Services.AddSingleton(ViewCatalog.Default);

// Storage: one storage context under all repositories. Only it knows about the files.
builder.Services.AddSingleton(provider => new DiskStorageContext(settings.Workspace, provider.GetRequiredService<IProjectWatcher>(),
    provider.GetRequiredService<OwnWrites>(), provider.GetRequiredService<ILogger<DiskStorageContext>>()));
builder.Services.AddSingleton<IStorageContext>(provider => provider.GetRequiredService<DiskStorageContext>());
builder.Services.AddSingleton<IProjectLock>(provider => provider.GetRequiredService<DiskStorageContext>());
builder.Services.AddSingleton<IProjectRepository, ProjectRepository>();
builder.Services.AddSingleton<IFolderRepository, FolderRepository>();
builder.Services.AddSingleton<IFolderPlaceRepository, FolderPlaceRepository>();
builder.Services.AddSingleton<IDocumentRepository, DocumentRepository>();
builder.Services.AddSingleton<IDocumentPlaceRepository, DocumentPlaceRepository>();
builder.Services.AddSingleton<ILinkRepository, LinkRepository>();

// Services: the interfaces in Odysseum.Abstractions, which the controllers and plugins use.
builder.Services.AddSingleton<IFolderService>(provider => new FolderService(provider.GetRequiredService<IFolderRepository>(),
    provider.GetRequiredService<IFolderPlaceRepository>(), provider.GetRequiredService<IDocumentRepository>(),
    provider.GetRequiredService<IDocumentPlaceRepository>(), provider.GetRequiredService<IProjectLock>(), settingsProvider,
    provider.GetRequiredService<ViewCatalog>()));
builder.Services.AddSingleton(provider => new ProjectTemplateService(provider.GetRequiredService<IFolderService>(),
    provider.GetRequiredService<IDocumentService>(), provider.GetRequiredService<IFolderRepository>(),
    provider.GetRequiredService<IDocumentRepository>(), provider.GetRequiredService<IFolderPlaceRepository>(),
    provider.GetRequiredService<IDocumentPlaceRepository>(), provider.GetRequiredService<IProjectRepository>(), provider.GetRequiredService<ViewCatalog>()));
builder.Services.AddSingleton<IProjectService>(provider => new ProjectService(provider.GetRequiredService<IProjectRepository>(),
    provider.GetRequiredService<IProjectLock>(), provider.GetRequiredService<ProjectTemplateService>(), templates));
builder.Services.AddSingleton<IDocumentService, DocumentService>();
builder.Services.AddSingleton<ILinkService, LinkService>();
builder.Services.AddSingleton(provider => new HistoryService(provider.GetRequiredService<IProjectRepository>(),
    provider.GetRequiredService<IFolderRepository>(), provider.GetRequiredService<IDocumentRepository>(), provider.GetRequiredService<ILinkRepository>(),
    provider.GetRequiredService<IDocumentPlaceRepository>(), provider.GetRequiredService<IProjectHistory>(), provider.GetRequiredService<IProjectLock>(),
    settings.VersionSeconds, provider.GetRequiredService<ILogger<HistoryService>>()));
builder.Services.AddSingleton<IHistoryService>(provider => provider.GetRequiredService<HistoryService>());
builder.Services.AddSingleton<ManuscriptExportService>();

// SignalR: each emitter sends the events of one service to the browsers.
builder.Services.AddSignalR()
    .AddJsonProtocol(json => json.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddSingleton<ProjectEventEmitter>();
builder.Services.AddSingleton<FolderEventEmitter>();
builder.Services.AddSingleton<DocumentEventEmitter>();
builder.Services.AddSingleton<LinkEventEmitter>();

builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

builder.Services.AddControllers()
    .AddJsonOptions(json => json.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)))
    .ConfigureApiBehaviorOptions(behavior => behavior.InvalidModelStateResponseFactory = context =>
    {
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

if (builder.Configuration.GetValue<int?>("ODYSSEUM_PARENT_PID") is { } parentId)
{
    try
    {
        var parent = System.Diagnostics.Process.GetProcessById(parentId);
        parent.EnableRaisingEvents = true;
        parent.Exited += (_, _) => app.Lifetime.StopApplication();
        app.Lifetime.ApplicationStopped.Register(parent.Dispose);
        if (parent.HasExited) return;
    }
    catch (ArgumentException) { return; }
}

DemoContent.Seed(settings);

// The repositories, services and emitters must exist before the projects are read, so that they get every change.
foreach (var type in new[] { typeof(IProjectRepository), typeof(IFolderRepository), typeof(IFolderPlaceRepository), typeof(IDocumentRepository),
    typeof(IDocumentPlaceRepository), typeof(ILinkRepository), typeof(IProjectService), typeof(IFolderService), typeof(IDocumentService),
    typeof(ILinkService), typeof(HistoryService), typeof(ProjectEventEmitter), typeof(FolderEventEmitter), typeof(DocumentEventEmitter),
    typeof(LinkEventEmitter) })
    app.Services.GetRequiredService(type);
await app.Services.GetRequiredService<IStorageContext>().LoadAllProjectsAsync();
await app.Services.GetRequiredService<HistoryService>().StartAsync();

app.UseMiddleware<ResponseHeadersMiddleware>();
app.UseMiddleware<ApiExceptionMiddleware>();
app.UseWebUi();
app.UseRouting();

app.UseAuthentication();

app.UseRateLimiter();

app.UseMiddleware<WorkspacePasswordMiddleware>();

app.MapOpenApi();
app.MapScalarApiReference(scalar => scalar.WithTitle("Odysseum API"));

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).ExcludeFromDescription();

app.MapControllers();
app.MapHub<ProjectHub>("/api/hub");

app.Run();
