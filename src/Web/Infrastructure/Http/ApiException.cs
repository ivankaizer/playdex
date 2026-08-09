using System.Net;

namespace Web.Infrastructure.Http;

public sealed class ApiException(HttpStatusCode statusCode, string detail) : Exception(detail)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
