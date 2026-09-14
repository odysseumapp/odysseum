using Odysseum.Server.API.Models;
using Odysseum.Server.Services;

namespace Odysseum.Server.API.Middleware;

/// <summary>Turns storage failures into error envelopes so controllers never catch filesystem exceptions themselves.</summary>
public class ApiExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiExceptionMiddleware> _logger;

    public ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (WorkspaceException ex) when (!context.Response.HasStarted)
        {
            await ApiResults.Error(ex.Status, ex.Message).ExecuteAsync(context);
        }
        catch (FileNotFoundException) when (!context.Response.HasStarted)
        {
            await ApiResults.NotFound("The file no longer exists. Your draft has been kept.").ExecuteAsync(context);
        }
        catch (DirectoryNotFoundException) when (!context.Response.HasStarted)
        {
            await ApiResults.NotFound("The folder no longer exists. Your draft has been kept.").ExecuteAsync(context);
        }
        catch (Exception ex) when (!context.Response.HasStarted && ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Workspace operation failed");
            await ApiResults.Unavailable("The workspace is temporarily unavailable or read-only. Your draft has been kept.").ExecuteAsync(context);
        }
    }
}
