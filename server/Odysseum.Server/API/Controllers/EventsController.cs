using Odysseum.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace Odysseum.Server.API.Controllers;

[ApiController]
[Route("api/projects/{project}/events")]
public class EventsController : ControllerBase
{
    private readonly ProjectLibrary _library;

    public EventsController(ProjectLibrary library)
    {
        _library = library;
    }

    /// <summary>Server-sent events: a "workspace" message whenever the project changes on disk, with heartbeats in between.</summary>
    [HttpGet]
    [Produces("text/event-stream")]
    public async Task Stream(string project)
    {
        var events = (await _library.OpenAsync(project)).Events;
        var response = HttpContext.Response;
        var aborted = HttpContext.RequestAborted;
        response.ContentType = "text/event-stream";
        response.Headers["X-Accel-Buffering"] = "no";
        var subscription = events.Subscribe();
        try
        {
            while (!aborted.IsCancellationRequested)
            {
                using var iteration = CancellationTokenSource.CreateLinkedTokenSource(aborted);
                var available = subscription.Reader.WaitToReadAsync(iteration.Token).AsTask();
                var heartbeat = Task.Delay(TimeSpan.FromSeconds(15), iteration.Token);
                var completed = await Task.WhenAny(available, heartbeat);
                await iteration.CancelAsync();
                aborted.ThrowIfCancellationRequested();
                if (completed == available)
                {
                    while (subscription.Reader.TryRead(out var revision))
                        await response.WriteAsync($"event: workspace\ndata: {revision}\n\n", aborted);
                }
                else await response.WriteAsync(": heartbeat\n\n", aborted);
                await response.Body.FlushAsync(aborted);
            }
        }
        catch (OperationCanceledException) when (aborted.IsCancellationRequested) { }
        finally { events.Unsubscribe(subscription.Id); }
    }
}
