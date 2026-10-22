using System.Text.RegularExpressions;
using Sentry;

namespace Api.Observability;

/// <summary>
/// Steam's Web API takes the user's key in the query string, so an outgoing call's URL is a secret. Breadcrumbs
/// recorded for HTTP calls carry that URL; this blanks secret query parameters before Sentry keeps them.
/// </summary>
public static partial class SensitiveUrls
{
    [GeneratedRegex(@"([?&](?:key|api_key|apikey|access_token|token)=)[^&#\s""]+", RegexOptions.IgnoreCase)]
    private static partial Regex SecretQueryParameter();

    public static string Redact(string value) => SecretQueryParameter().Replace(value, "$1[redacted]");

    // Runs as the breadcrumb is added, so the new one's timestamp is the same moment.
    public static Breadcrumb Redact(Breadcrumb breadcrumb) => new(
        Redact(breadcrumb.Message ?? ""),
        breadcrumb.Type ?? "default",
        breadcrumb.Data?.ToDictionary(pair => pair.Key, pair => Redact(pair.Value)),
        breadcrumb.Category,
        breadcrumb.Level);
}
