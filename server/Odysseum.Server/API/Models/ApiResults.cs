using Microsoft.AspNetCore.Mvc;

namespace Odysseum.Server.API.Models;

public class SuccessResponse<T>
{
    public string ApiVersion { get; set; }
    public T Data { get; set; }

    public SuccessResponse(string apiVersion, T data)
    {
        ApiVersion = apiVersion;
        Data = data;
    }
}

public class ErrorResponse
{
    public string ApiVersion { get; set; }
    public ApiError Error { get; set; }

    public ErrorResponse(string apiVersion, ApiError error)
    {
        ApiVersion = apiVersion;
        Error = error;
    }
}

public class ApiError
{
    public int Code { get; }
    public string Message { get; }

    public ApiError(int code, string message)
    {
        Code = code;
        Message = message;
    }
}

public class ApiCollection<T>
{
    public int TotalItems { get; }
    public IEnumerable<T> Items { get; }

    public ApiCollection(IEnumerable<T> items, int? total = null)
    {
        Items = items ?? Array.Empty<T>();
        TotalItems = total ?? Items.Count();
    }
}

/// <summary>Every API response is built here so the envelope is the same everywhere.</summary>
public static class ApiResults
{
    private const string ApiVersion = "1.0";

    private static IResult Response<T>(T data, int statusCode, string? location = null)
    {
        var response = TypedResults.Json(new SuccessResponse<T>(ApiVersion, data), statusCode: statusCode);
        if (location != null)
        {
            return new HeaderResult(response, location);
        }
        return response;
    }

    // Success responses
    public static IResult Success<T>(T data) =>
        Response(data, StatusCodes.Status200OK);

    public static IResult SuccessCollection<T>(IEnumerable<T>? items, int? total = null) =>
        Success(new ApiCollection<T>(items ?? Array.Empty<T>(), total));

    public static IResult Created<T>(T data, string? uri = null) =>
        Response(data, StatusCodes.Status201Created, uri);

    public static IResult File(byte[] content, string contentType, string fileName) =>
        TypedResults.File(content, contentType, fileName);

    // Error responses
    public static IResult Error(int statusCode, string message) =>
        TypedResults.Json(new ErrorResponse(ApiVersion, new ApiError(statusCode, message)), statusCode: statusCode);

    public static IResult BadRequest(string message) =>
        Error(StatusCodes.Status400BadRequest, message);

    public static IResult Unauthorized(string message) =>
        Error(StatusCodes.Status401Unauthorized, message);

    public static IResult Forbidden(string message) =>
        Error(StatusCodes.Status403Forbidden, message);

    public static IResult NotFound(string message) =>
        Error(StatusCodes.Status404NotFound, message);

    public static IResult Conflict(string message) =>
        Error(StatusCodes.Status409Conflict, message);

    public static IResult TooManyRequests(string message) =>
        Error(StatusCodes.Status429TooManyRequests, message);

    public static IResult Unavailable(string message) =>
        Error(StatusCodes.Status503ServiceUnavailable, message);

    public static IResult ServerError(string message = "Error occurred") =>
        Error(StatusCodes.Status500InternalServerError, message);

    /// <summary>For MVC hooks that require an IActionResult, such as the automatic model validation response.</summary>
    public static IActionResult ToActionResult(IResult result) => new ActionResultAdapter(result);

    private class HeaderResult : IResult
    {
        private readonly IResult _result;
        private readonly string _locationValue;

        public HeaderResult(IResult result, string locationValue)
        {
            _result = result;
            _locationValue = locationValue;
        }

        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers.Location = _locationValue;
            await _result.ExecuteAsync(httpContext);
        }
    }

    private class ActionResultAdapter : IActionResult
    {
        private readonly IResult _result;

        public ActionResultAdapter(IResult result)
        {
            _result = result;
        }

        public Task ExecuteResultAsync(ActionContext context) => _result.ExecuteAsync(context.HttpContext);
    }
}
