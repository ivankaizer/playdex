using System.Net;
using System.Text;
using System.Text.Json;
using Api.Infrastructure.Http;
using Api.Modules.Manual.Drafts.Extraction;
using Microsoft.Extensions.Options;
using SkiaSharp;
using Xunit;

namespace Api.Tests;

public sealed class PlaySessionExtractorTests
{
    private static ExtractionInput Input(string? text = "Played Hades yesterday for an hour") =>
        new(text, null, null, new DateOnly(2026, 9, 9), ["unknown", "pc", "steam", "ps5"]);

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void EmptyInputIsRejected(string? text) =>
        Assert.Throws<AppProblemException>(() => ExtractionInputValidator.Validate(Input(text), new()));

    [Fact]
    public void LimitsAndInvalidImagesAreRejected()
    {
        Assert.Throws<AppProblemException>(() => ExtractionInputValidator.Validate(Input("abcd"), new() { MaxTextCharacters = 3 }));
        Assert.Throws<AppProblemException>(() => ExtractionInputValidator.Validate(Input() with { Image = [1, 2, 3], ImageContentType = "image/png" }, new()));
        using var bitmap = new SKBitmap(2, 2);
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var input = Input() with { Image = encoded.ToArray(), ImageContentType = "image/png" };
        ExtractionInputValidator.Validate(input, new());
        Assert.Throws<AppProblemException>(() => ExtractionInputValidator.Validate(input, new() { MaxImagePixels = 3 }));
        Assert.Throws<AppProblemException>(() => ExtractionInputValidator.Validate(input with { ImageContentType = "image/jpeg" }, new()));
        Assert.Throws<AppProblemException>(() => ExtractionInputValidator.Validate(input, new() { MaxImageBytes = 1 }));
    }

    [Fact]
    public async Task SendsStructuredEvidenceWithoutToolsOrStorageAndNormalizesUncertainty()
    {
        var handler = new FakeHandler("""{"sessions":[{"title":" Hades ","date":"09/08/26","durationMinutes":0,"platformKey":"PSN","warnings":["Conflicting date"]}]}""");
        var result = await Extractor(handler).Extract(Input(), CancellationToken.None);
        var row = Assert.Single(result);
        Assert.Equal("Hades", row.Title);
        Assert.Null(row.Date);
        Assert.Null(row.DurationMinutes);
        Assert.Equal("unknown", row.PlatformKey);
        Assert.Contains(row.Warnings, warning => warning.StartsWith("Date:", StringComparison.Ordinal));
        Assert.Contains(row.Warnings, warning => warning.StartsWith("Duration:", StringComparison.Ordinal));
        Assert.Contains(row.Warnings, warning => warning.StartsWith("Platform:", StringComparison.Ordinal));
        using var request = JsonDocument.Parse(handler.Body!);
        Assert.False(request.RootElement.GetProperty("store").GetBoolean());
        Assert.False(request.RootElement.TryGetProperty("tools", out _));
        Assert.True(request.RootElement.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
        Assert.Contains("2026-09-09", handler.Body);
    }

    [Fact]
    public async Task PartialNumericDatesUseTheReferenceMonthAndYear()
    {
        var handler = new FakeHandler("""{"sessions":[{"title":"Hades","date":"27","durationMinutes":60,"platformKey":"pc","warnings":[]},{"title":"Celeste","date":"27.06","durationMinutes":30,"platformKey":"pc","warnings":[]}]}""");

        var rows = await Extractor(handler).Extract(Input("Played Hades on 27 and Celeste on 27.06"), CancellationToken.None);

        Assert.Equal("2026-09-27", rows[0].Date);
        Assert.Equal("2026-06-27", rows[1].Date);
        Assert.Contains("27 means 2026-09-27", handler.Body);
        Assert.Contains("27.06 means 2026-06-27", handler.Body);
    }

    [Fact]
    public async Task OmittedDateUsesTheLocalReferenceDate()
    {
        var handler = new FakeHandler("""{"sessions":[{"title":"Hades","date":null,"durationMinutes":60,"platformKey":"pc","warnings":[]}]}""");

        var row = Assert.Single(await Extractor(handler).Extract(Input("Played Hades for an hour"), CancellationToken.None));

        Assert.Equal("2026-09-09", row.Date);
        Assert.DoesNotContain(row.Warnings, warning => warning.StartsWith("Date:", StringComparison.Ordinal));
        Assert.Contains("If no date is provided, use 2026-09-09", handler.Body);
    }

    [Fact]
    public async Task IncludesDecodedImageAndText()
    {
        using var bitmap = new SKBitmap(2, 2);
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var handler = new FakeHandler("{\"sessions\":[]}");
        Assert.Empty(await Extractor(handler).Extract(Input() with { Image = encoded.ToArray(), ImageContentType = "image/png" }, CancellationToken.None));
        Assert.Contains("data:image/png;base64,", handler.Body);
        Assert.Contains("Played Hades", handler.Body);
    }

    [Theory]
    [InlineData("{\"sessions\":[{\"title\":\"Hades\"}]}")]
    [InlineData("{\"sessions\":[] ,\"gameId\":1}")]
    [InlineData("not json")]
    public async Task InvalidOutputIsRejected(string output) =>
        Assert.Equal(502, (await Assert.ThrowsAsync<AppProblemException>(() => Extractor(new(output)).Extract(Input(), CancellationToken.None))).StatusCode);

    [Fact]
    public async Task ExcessRowsAreRejectedWithoutTruncation()
    {
        const string row = "{\"title\":\"Hades\",\"date\":null,\"durationMinutes\":null,\"platformKey\":null,\"warnings\":[]}";
        var handler = new FakeHandler("{\"sessions\":[" + row + "," + row + "]}");
        await Assert.ThrowsAsync<AppProblemException>(() => Extractor(handler, new() { Enabled = true, ApiKey = "test", Model = "test", MaxRows = 1 }).Extract(Input(), CancellationToken.None));
    }

    [Fact]
    public async Task ProviderErrorsDoNotExposeBody()
    {
        var handler = new FakeHandler("secret raw source") { Status = HttpStatusCode.TooManyRequests };
        var error = await Assert.ThrowsAsync<AppProblemException>(() => Extractor(handler).Extract(Input(), CancellationToken.None));
        Assert.DoesNotContain("secret", error.ToString());
    }

    [Theory]
    [InlineData("refusal")]
    [InlineData("function_call")]
    public async Task RefusalsAndUnexpectedOutputAreRejected(string type)
    {
        var handler = new FakeHandler("private source") { OutputType = type };
        await Assert.ThrowsAsync<AppProblemException>(() => Extractor(handler).Extract(Input(), CancellationToken.None));
    }

    [Fact]
    public async Task DisabledConfigurationDoesNotContactProvider()
    {
        var handler = new FakeHandler("{}");
        Assert.Equal(503, (await Assert.ThrowsAsync<AppProblemException>(() => Extractor(handler, new()).Extract(Input(), CancellationToken.None))).StatusCode);
        Assert.Null(handler.Body);
    }

    [Fact]
    public async Task TimeoutIsRetryableAndCallerCancellationPropagates()
    {
        var handler = new FakeHandler("{}") { WaitForCancellation = true };
        Assert.Equal(504, (await Assert.ThrowsAsync<AppProblemException>(() => Extractor(handler, new() { Enabled = true, Model = "test", ApiKey = "test", TimeoutSeconds = 1 }).Extract(Input(), CancellationToken.None))).StatusCode);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Extractor(handler).Extract(Input(), cancellation.Token));
    }

    [Fact]
    public async Task ValidRowsRetainValuesAndCalendarOverflowRequiresCorrection()
    {
        var handler = new FakeHandler("""{"sessions":[{"title":"Hades","date":"2026-09-08","durationMinutes":60,"platformKey":"pc","warnings":[]},{"title":"Hades","date":"9999-12-31","durationMinutes":1440,"platformKey":"pc","warnings":[]}]}""");
        var rows = await Extractor(handler).Extract(Input(), CancellationToken.None);
        Assert.Equal("2026-09-08", rows[0].Date);
        Assert.Equal(60, rows[0].DurationMinutes);
        Assert.Equal("pc", rows[0].PlatformKey);
        Assert.Empty(rows[0].Warnings);
        Assert.Null(rows[1].DurationMinutes);
    }

    [Fact]
    public async Task FieldWarningsKeepOtherwiseValidValuesUnresolved()
    {
        var handler = new FakeHandler("""{"sessions":[{"title":"Hades","date":"2026-09-08","durationMinutes":60,"platformKey":"pc","warnings":["Date: conflicting image and text","Duration: uncertain"]}]}""");
        var row = Assert.Single(await Extractor(handler).Extract(Input(), CancellationToken.None));
        Assert.Null(row.Date);
        Assert.Null(row.DurationMinutes);
    }

    [Fact]
    public async Task OversizedAndIncompleteProviderResponsesAreRejected()
    {
        await Assert.ThrowsAsync<AppProblemException>(() => Extractor(new(new string('x', 1_048_576))).Extract(Input(), CancellationToken.None));
        await Assert.ThrowsAsync<AppProblemException>(() => Extractor(new("{\"sessions\":[]}") { ResponseStatus = "incomplete" }).Extract(Input(), CancellationToken.None));
    }

    private static OpenAiPlaySessionExtractor Extractor(FakeHandler handler, AiDraftOptions? options = null) =>
        new(new HttpClient(handler), Options.Create(options ?? new() { Enabled = true, ApiKey = "test", Model = "test" }));

    private sealed class FakeHandler(string output) : HttpMessageHandler
    {
        public string ResponseStatus { get; init; } = "completed";
        public string OutputType { get; init; } = "output_text";
        public bool WaitForCancellation { get; init; }
        public string? Body { get; private set; }
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (WaitForCancellation) await Task.Delay(Timeout.Infinite, cancellationToken);
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(Status) { Content = new StringContent(JsonSerializer.Serialize(new { status = ResponseStatus, output = new[] { new { type = "message", content = new[] { new { type = OutputType, text = output } } } } }), Encoding.UTF8, "application/json") };
        }
    }
}
