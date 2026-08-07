using System.Text.Json;

namespace Playdex.Api.Shared.Http;

public sealed class ApiEnvelopeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api") ||
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

        bufferedBody.Position = 0;
        if (context.Response.StatusCode == StatusCodes.Status204NoContent)
        {
            context.Response.ContentLength = null;
            context.Response.ContentType = null;
            return;
        }

        context.Response.ContentLength = null;
        context.Response.ContentType = "application/json";

        var writer = new Utf8JsonWriter(originalBody);
        writer.WriteStartObject();
        if (context.Response.StatusCode is >= 200 and < 300)
        {
            writer.WritePropertyName("data");
            await WriteBodyAsync(bufferedBody, writer, context.RequestAborted);
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
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
        await writer.FlushAsync(context.RequestAborted);
    }

    private static async Task WriteBodyAsync(
        Stream body,
        Utf8JsonWriter writer,
        CancellationToken cancellationToken)
    {
        if (body.Length == 0)
        {
            writer.WriteNullValue();
            return;
        }

        using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        document.RootElement.WriteTo(writer);
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
                if (TryReadString(root, "detail", out var detail)) return detail;
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
