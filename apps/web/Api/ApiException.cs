using System.Net;

namespace Playdex.Web.Api;

public sealed class ApiException(HttpStatusCode statusCode, string detail) : Exception(detail)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
