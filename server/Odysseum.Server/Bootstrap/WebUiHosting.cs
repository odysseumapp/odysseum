using Odysseum.Server.Services.WebUi;

namespace Odysseum.Server.Bootstrap;

public static class WebUiHosting
{
    /// <summary>Serves the installed static UI. Register before routing so the file middleware is not skipped by the SPA fallback endpoint.</summary>
    public static void UseWebUi(this WebApplication app)
    {
        var files = app.Services.GetRequiredService<WebUiFileProvider>();
        app.UseStaticFiles(new StaticFileOptions
        {
            RequestPath = "/webui", FileProvider = files,
            OnPrepareResponse = context =>
            {
                context.Context.Response.Headers.CacheControl = context.Context.Request.Path.StartsWithSegments("/webui/_nuxt")
                    ? "public, max-age=31536000, immutable" : "no-cache";
            },
        });
        app.MapGet("/", () => Results.Redirect("/webui/", permanent: false)).ExcludeFromDescription();
        app.MapFallback("/webui/{**path}", async context =>
        {
            var requestPath = context.Request.Path.Value!;
            // Relative asset URLs in index.html resolve against the directory, so the base path needs its trailing slash.
            if (requestPath == "/webui") { context.Response.Redirect("/webui/"); return; }
            var path = requestPath["/webui".Length..];
            if ((!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
                || path.StartsWith("/_nuxt/", StringComparison.Ordinal)
                || (Path.HasExtension(path) && !path.StartsWith("/p/", StringComparison.Ordinal)))
            { context.Response.StatusCode = StatusCodes.Status404NotFound; return; }
            var index = files.GetFileInfo("/index.html");
            context.Response.Headers.CacheControl = "no-cache";
            if (!index.Exists)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsync("The web UI is not installed. Install a UI release with --update-webui latest or --install-webui <archive.zip>. The API remains available at /api and /scalar.");
                return;
            }
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength = index.Length;
            if (!HttpMethods.IsHead(context.Request.Method)) await context.Response.SendFileAsync(index);
        }).ExcludeFromDescription();
    }
}
