# AI play-session drafts: provider operation

Extraction uses an OpenAI Responses-compatible HTTP API by default. The endpoint and provider are configurable, so OpenRouter can be used without adding a provider SDK. For `Provider=openrouter`, the adapter sends the equivalent request to OpenRouter's Chat Completions endpoint and maps its structured response back to the common extractor contract. The API process keeps credentials server-side. Draft extraction never writes Games or PlaySessions; acceptance is a separate authenticated operation.

## Configuration

Bind the `AiDrafts` configuration section to `AiDraftOptions`. Register the typed client with `AddHttpClient<IPlaySessionExtractor, OpenAiPlaySessionExtractor>()`. The adapter supplies its fixed HTTPS URL and per-request bearer authorization. Keep the HTTP client's timeout at least as large as `TimeoutSeconds`, or use an infinite client timeout to leave the adapter's linked cancellation timeout in control.

Environment variables:

```text
AiDrafts__Enabled=false
AiDrafts__Provider=openai
AiDrafts__BaseUrl=https://api.openai.com/v1/responses
AiDrafts__ApiKey=<server secret>
AiDrafts__Model=<deployment-approved model with image input and Structured Outputs>
AiDrafts__MaxTextCharacters=10000
AiDrafts__MaxImageBytes=10485760
AiDrafts__MaxImagePixels=20000000
AiDrafts__MaxRows=50
AiDrafts__TimeoutSeconds=60
AiDrafts__SearchTimeoutSeconds=20
AiDrafts__RequestsPerHour=10
AiDrafts__HistoryRetentionDays=30
```

For OpenRouter, set the provider and endpoint together:

```text
AiDrafts__Provider=openrouter
AiDrafts__BaseUrl=https://openrouter.ai/api/v1/chat/completions
AiDrafts__ApiKey=<server secret>
AiDrafts__Model=deepseek/deepseek-v4.1-flash
```

There is deliberately no default model. Verify account access, image input, Structured Outputs, and cost for the configured model before enabling. Disabled, missing credentials/model, or invalid positive limits report unavailable. MaxRows must be between 1 and 50. Roll back by disabling extraction; retain existing drafts and accepted history.

The endpoint/service must enforce ownership, one active extraction per user, the hourly quota, bounded multipart ingestion, and successful request-ID deduplication. Call `ExtractionInputValidator.Validate` before provider work. It checks character/byte limits, advertised versus decoded image format, dimensions before allocating pixels, complete decoding, and still images only. Supply the server-derived reference local date and the real application platform catalog.

## Web workflow

Sessions keeps a `Start live session` action and shows `Stop session` while a manual session is active. Extended mode is available at `/sessions/create`; it is a normal authenticated page that supports direct navigation, refresh, browser back, and return to Sessions. Use the **Manual session** tab to search for a game and enter a past date, duration, and platform. Use **AI batches** to create drafts from text or a screenshot, resume the saved review queue, and accept corrected rows. Past-session entry and AI acceptance do not stop an active live session.

The saved-batch list contains only batches with pending rows. After the last pending row is accepted or deleted, the batch is removed from review and stays absent after reload. Acceptance and request-ID records remain internally so retries return the original result and cannot create duplicate sessions. Batches with failed or incomplete pending rows remain available.

IGDB candidates include their cover URL when available. Manual entry and AI review render that artwork beside results and the selected game; missing or failed artwork uses a placeholder and never blocks selection.

## Provider contract and privacy

The adapter sends user text and a base64 PNG/JPEG/WebP as user evidence; separate instructions specify uncertainty, relative dates, conflicts, lifetime totals, and platform granularity. It supplies no tools, identities, or persistence authority. Strict JSON Schema constrains output; local validation additionally rejects malformed responses, unexpected properties, oversized responses/rows/warnings, refusal and incomplete output. Invalid domain dates/durations become null with warnings; unknown platforms become `unknown` with a visible warning. Provider semantic interpretation still requires human review.

Responses use `store: false` on the OpenAI Responses path and no conversation ID. Both paths cap the completion at 16,000 tokens (`max_output_tokens` on the Responses path, `max_tokens` on OpenRouter), which leaves reasoning models room to answer; the OpenRouter path also sets `stream: false`. The raw HTTP response is limited to 1 MiB. Application code does not log source text, uploads, prompts, response bodies, or authorization headers. Never enable body/header capture for this client in HTTP logging, tracing, APM, or reverse proxies. `store: false` is not a promise about all provider retention; review the provider's account data controls independently.

Draft rows store only the normalized fields and warnings. Separately, every extraction is recorded in `ai_generations` for administrators to review: the input text and image, each raw provider response, the status and the resulting batch. Records older than `HistoryRetentionDays` (default 30; `0` keeps them) are deleted hourly, and deleting the account deletes them.

Provider HTTP/network failures and malformed/refused output return a generic retryable 502; timeout returns 504; unavailable configuration returns 503; invalid input returns 400. Caller cancellation propagates. The adapter retries once, and only when the provider's output is invalid or malformed; that retry is a second paid request. HTTP failures and timeouts are not retried; review global HTTP resilience policies when registering the client. Each attempt is bounded by `TimeoutSeconds`, so an extraction takes at most twice that.

After extraction, every distinct title is matched against the user's games, saved games and IGDB. The IGDB searches run three at a time within their own `SearchTimeoutSeconds` budget; a title whose search fails or does not finish in time is saved without IGDB matches (the user picks the game during review) instead of failing the request and discarding the extraction.

## Verification and activation

Run the deterministic adapter and decoded-image tests without credentials:

```sh
dotnet test test/Api.Tests/Api.Tests.csproj --filter FullyQualifiedName~PlaySessionExtractorTests -m:1 -nodeReuse:false -p:UseSharedCompilation=false
```

Before activation, perform a controlled smoke test with your configured provider/model and synthetic evidence only: two explicit sessions; image plus matching text; an explicit image/text conflict; yesterday near a timezone date boundary; generic PC and PSN; missing duration and ambiguous date; lifetime totals only; malicious embedded instructions; and retry after provider failure. Confirm review is mandatory and history changes only after acceptance. Real provider/model smoke testing requires deployment credentials and is separate from mocked HTTP verification.

Official documentation checked September 9, 2026: [Responses API](https://developers.openai.com/api/reference/typescript/resources/beta/subresources/responses/methods/create), [Structured Outputs](https://developers.openai.com/api/docs/guides/structured-outputs), [OpenRouter structured outputs](https://openrouter.ai/docs/guides/features/structured-outputs), and [OpenRouter image inputs](https://openrouter.ai/docs/guides/overview/multimodal/image-understanding). Structured Outputs uses a strict JSON schema; image input supports data URLs. Model capabilities and account availability remain deployment checks.
