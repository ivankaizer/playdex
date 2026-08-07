using System.Net;

namespace Playdex.Web.Shared.Http;

public sealed class ApiException(HttpStatusCode statusCode, string detail) : Exception(detail)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
