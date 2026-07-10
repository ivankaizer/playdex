namespace Playdex.Api.Http;

public sealed record ErrorResponse(int StatusCode, ApiResponse<object> Envelope);

public static class ErrorResponseFactory
{
    public static ErrorResponse FromException(Exception exception) =>
        exception is AppProblem problem
            ? new ErrorResponse(problem.StatusCode, problem.ToEnvelope())
            : new ErrorResponse(500, ApiResponse<object>.Fail("Internal server error."));
}
