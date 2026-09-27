using Microsoft.AspNetCore.Mvc.Filters;
using Odysseum.Server.API.Models;

namespace Odysseum.Server.API.Filters;

/// <summary>Refuses a change request without an If-Match header with error 428. The header holds the ETag of the item
/// as the browser last read it; the server refuses the change with error 412 when the item changed since then.</summary>
public sealed class RequireIfMatchAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (string.IsNullOrWhiteSpace(context.HttpContext.Request.Headers.IfMatch))
            context.Result = ApiResults.ToActionResult(ApiResults.PreconditionRequired("Send the item's ETag in the If-Match header."));
    }
}
