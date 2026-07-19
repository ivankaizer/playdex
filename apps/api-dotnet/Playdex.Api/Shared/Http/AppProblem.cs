namespace Playdex.Api.Shared.Http;

public sealed class AppProblem(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public static AppProblem Validation(string message) => new(400, message);
    public static AppProblem Unauthorized(string message = "Unauthorized.") => new(401, message);
    public static AppProblem Forbidden(string message = "Forbidden.") => new(403, message);
    public static AppProblem NotFound(string message = "Not found.") => new(404, message);
    public static AppProblem Conflict(string message) => new(409, message);
}
