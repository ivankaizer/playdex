using System.Net;

namespace Web.Shared.Http;

public sealed class ApiException(HttpStatusCode statusCode, string detail) : Exception(detail)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
