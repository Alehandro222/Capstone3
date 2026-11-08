using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TrackerApi.Services;

// Thrown by the services when a request breaks a rule ("That computer was just taken").
// TrackerExceptionFilter turns it into an HTTP error with a { "message": "..." } body,
// the same shape the controllers already use, so the frontends can show the text.
public class TrackerException : Exception
{
    public int StatusCode { get; }

    public TrackerException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public static TrackerException NotFound(string message) => new(StatusCodes.Status404NotFound, message);
    public static TrackerException Conflict(string message) => new(StatusCodes.Status409Conflict, message);
    public static TrackerException BadRequest(string message) => new(StatusCodes.Status400BadRequest, message);
}

public class TrackerExceptionFilter : IExceptionFilter
{
    // Used when the body cannot be read at all (bad JSON, unknown enum value, missing body).
    // Names the field instead of returning ASP.NET's technical error list.
    public static IActionResult UnreadableRequest(ActionContext context)
    {
        // JSON errors are keyed like "$.sectors[0]"; prefer them over the generic "request" entry.
        var keys = context.ModelState.Where(e => e.Value!.Errors.Count > 0).Select(e => e.Key).ToList();
        var key = keys.FirstOrDefault(k => k.StartsWith('$')) ?? keys.FirstOrDefault();
        var field = key?.TrimStart('$', '.').Split('[', '.')[0];
        var message = string.IsNullOrEmpty(field) || field == "request"
            ? "The request could not be read. Check the information and try again."
            : $"The value for '{field}' is not valid.";
        return new BadRequestObjectResult(new { message });
    }

    public void OnException(ExceptionContext context)
    {
        if (context.Exception is TrackerException ex)
        {
            context.Result = new ObjectResult(new { message = ex.Message }) { StatusCode = ex.StatusCode };
            context.ExceptionHandled = true;
        }
    }
}
