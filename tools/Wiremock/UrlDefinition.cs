using System.Net;

namespace Wiremock;

public record struct UrlDefinition(string Name, HttpMethod Method, string Path, string[] States, HttpStatusCode StatusCode = HttpStatusCode.OK);
