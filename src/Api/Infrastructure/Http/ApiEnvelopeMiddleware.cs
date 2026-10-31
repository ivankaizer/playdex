using System.Text.Json;

namespace Api.Infrastructure.Http;

public sealed class ApiEnvelopeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // MCP speaks JSON-RPC (and streams it as server-sent events) and OAuth clients expect RFC 6749 token
        // responses; an envelope would break both.
        if (!context.Request.Path.StartsWithSegments("/api") ||
            context.Request.Path.StartsWithSegments("/api/mcp") ||
            context.Request.Path.StartsWithSegments("/api/oauth/token") ||
            HttpMethods.IsOptions(context.Request.Method))
        {
            await next(context);
            return;
        }

        var originalBody = context.Response.Body;
        await using var bufferedBody = new MemoryStream();
        context.Response.Body = bufferedBody;

        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        if (context.RequestAborted.IsCancellationRequested)
        {
            // The client has gone; there is nobody to send an envelope to.
            return;
        }

        bufferedBody.Position = 0;
        if (context.Response.StatusCode == StatusCodes.Status204NoContent)
        {
            context.Response.ContentLength = null;
            context.Response.ContentType = null;
            return;
        }

        var success = context.Response.StatusCode is >= 200 and < 300;
        using var data = success ? await TryParseJsonBodyAsync(bufferedBody, context) : null;
        if (success && data is null && bufferedBody.Length > 0)
        {
            // Files (e.g. admin screenshots), text and anything else that is not JSON pass through as written; only
            // JSON payloads are enveloped.
            bufferedBody.Position = 0;
            context.Response.ContentLength = bufferedBody.Length;
            await bufferedBody.CopyToAsync(originalBody, context.RequestAborted);
            return;
        }

        context.Response.ContentLength = null;
        context.Response.ContentType = "application/json";

        await using var writer = new Utf8JsonWriter(originalBody);
        writer.WriteStartObject();
        if (success)
        {
            writer.WritePropertyName("data");
            if (data is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                data.RootElement.WriteTo(writer);
            }

            writer.WriteNull("error");
        }
        else
        {
            writer.WriteNull("data");
            writer.WriteStartObject("error");
            writer.WriteString("error", await ReadErrorAsync(
                bufferedBody,
                context.Response.StatusCode,
                context.RequestAborted));
            writer.WriteString("correlationId", context.TraceIdentifier);
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
        await writer.FlushAsync(context.RequestAborted);
    }

    /// <summary>
    /// The body as JSON, or null when it is empty, not declared as JSON, or not valid JSON. Parsed before anything
    /// is written, so a body that fails to parse can still be passed through instead of breaking the response.
    /// </summary>
    private static async Task<JsonDocument?> TryParseJsonBodyAsync(MemoryStream body, HttpContext context)
    {
        if (body.Length == 0 || !IsJson(context.Response.ContentType))
        {
            return null;
        }

        try
        {
            return await JsonDocument.ParseAsync(body, cancellationToken: context.RequestAborted);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsJson(string? contentType)
    {
        if (contentType is null)
        {
            return true;
        }

        var mediaType = contentType.Split(';', 2)[0].Trim();
        return mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
               mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ReadErrorAsync(
        Stream body,
        int statusCode,
        CancellationToken cancellationToken)
    {
        if (body.Length > 0)
        {
            try
            {
                using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
                var root = document.RootElement;
                if (TryReadString(root, "detail", out var detail))
                {
                    return detail;
                }

                if (root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty("error", out var error) &&
                    TryReadString(error, "error", out var nestedError))
                {
                    return nestedError;
                }
            }
            catch (JsonException)
            {
            }
        }

        return statusCode switch
        {
            StatusCodes.Status400BadRequest => "Bad request.",
            StatusCodes.Status401Unauthorized => "Unauthorized.",
            StatusCodes.Status403Forbidden => "Forbidden.",
            StatusCodes.Status404NotFound => "Route not found.",
            _ when statusCode >= 500 => "Internal server error.",
            _ => "Request failed.",
        };
    }

    private static bool TryReadString(JsonElement parent, string propertyName, out string value)
    {
        value = "";
        if (parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            return false;
        }

        value = property.GetString()!;
        return true;
    }
}
