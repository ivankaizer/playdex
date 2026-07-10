namespace Playdex.Api.Http;

public sealed record ApiErrorResponse(string Error);

public sealed record ApiResponse<T>(T? Data, ApiErrorResponse? Error)
{
    public static ApiResponse<T> Ok(T data) => new(data, null);

    public static ApiResponse<T> Fail(string message) => new(default, new ApiErrorResponse(message));
}
