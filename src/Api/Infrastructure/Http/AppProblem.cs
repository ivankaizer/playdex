namespace Api.Infrastructure.Http;

public sealed class AppProblemException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public static AppProblemException Validation(string message) => new(400, message);
    public static AppProblemException Unauthorized(string message = "Unauthorized.") => new(401, message);
    public static AppProblemException Forbidden(string message = "Forbidden.") => new(403, message);
    public static AppProblemException NotFound(string message = "Not found.") => new(404, message);
    public static AppProblemException Conflict(string message) => new(409, message);
}
