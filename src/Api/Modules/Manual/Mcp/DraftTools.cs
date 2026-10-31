using System.ComponentModel;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Api.Infrastructure.Auth;
using Api.Infrastructure.Common;
using Api.Infrastructure.Http;
using Api.Modules.Manual.Drafts;
using Api.Modules.Manual.Drafts.Extraction;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Api.Modules.Manual.Mcp;

public sealed record DraftSessionInput(
    [property: Description("Game title as the user wrote it.")] string Title,
    [property: Description("Day played, yyyy-MM-dd, in the user's time zone.")] string? Date,
    [property: Description("Minutes played.")] int? DurationMinutes,
    [property: Description("Platform key: pc, steam, ps5, ps4, ps3, switch-1, switch-2, xbox-series-xs, gog; omit when the user did not say.")] string? Platform);

/// <summary>
/// The MCP tools behind the draft card. The model sees only <c>draft_sessions</c>, which saves nothing but a pending
/// batch; the card's own tools (<c>visibility: ["app"]</c>) change, delete and accept its rows, and each of them needs
/// the batch's <see cref="DraftConfirmation"/> token, which the model never sees.
/// </summary>
[McpServerToolType]
public sealed class DraftTools(DraftService drafts, ManualService manual, DraftConfirmation confirmation,
    IOptions<AiDraftOptions> options, TimeProvider clock)
{
    /// <summary>The OAuth scope that lets a client use these tools.</summary>
    public const string Scope = "drafts";

    private static readonly JsonSerializerOptions _json = JsonSerializerOptions.Web;

    [McpServerTool(Name = "draft_sessions", Title = "Draft play sessions", Destructive = false, Idempotent = false,
        OpenWorld = false, ReadOnly = false)]
    [McpMeta("ui", JsonValue = $$"""{"resourceUri":"{{DraftCard.Uri}}","visibility":["model"]}""")]
    [McpMeta("openai/outputTemplate", DraftCard.Uri)]
    [McpMeta("openai/widgetAccessible", true)]
    [Description("""
        Log play sessions the user describes ("2h of Hades yesterday on Switch", a screenshot of playtime) to their
        Playdex history. Parse every session yourself, one row per game per day. Nothing is saved yet: this shows the
        user a card where they check the matched games and accept. Do not claim the sessions are logged until they do.
        """)]
    public async Task<CallToolResult> DraftSessions(
        ClaimsPrincipal user,
        [Description("The sessions, one per game per day.")] DraftSessionInput[] sessions,
        [Description("The user's IANA time zone, e.g. Europe/Warsaw. Dates are days there. Defaults to UTC.")] string? timezone = null,
        CancellationToken ct = default) => await Run(async () =>
        {
            var userId = user.GetUserId();
            var rows = (sessions ?? []).Select(s => new ExtractedSession(s.Title, s.Date, s.DurationMinutes, s.Platform, []))
                .ToArray();
            var batch = await drafts.CreateFromRows(userId, string.IsNullOrWhiteSpace(timezone) ? "UTC" : timezone.Trim(),
                rows, TimeSpan.FromSeconds(Math.Max(1, options.Value.SearchTimeoutSeconds)), clock.GetUtcNow(), ct);
            var platforms = (await manual.ListPlatforms(ct)).Platforms
                .Where(p => p.Key != ManualService.PlaynitePlatformKey).ToArray();
            var summary = Summary(batch);
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = summary }],
                // Some clients (Claude Code) hand the model the structured content instead of the text, so the
                // summary, and with it "nothing is logged yet", travels in both.
                StructuredContent = JsonSerializer.SerializeToElement(new { message = summary, batch, platforms }, _json),
                Meta = new JsonObject { ["confirmToken"] = confirmation.Issue(userId, batch.Id) },
            };
        });

    [McpServerTool(Name = "search_games", Title = "Search games", ReadOnly = true, OpenWorld = false)]
    [McpMeta("ui", JsonValue = """{"visibility":["app"]}""")]
    [Description("Games a draft title may mean: ones the user played, saved games, then IGDB.")]
    public async Task<CallToolResult> SearchGames(ClaimsPrincipal user, string query, CancellationToken ct) =>
        await Run(async () => Structured(new
        {
            results = await drafts.SearchCandidates(user.GetUserId(), SearchText.Bounded(query), ct),
        }));

    [McpServerTool(Name = "update_draft", Title = "Update a draft", Destructive = false, Idempotent = true, OpenWorld = false)]
    [McpMeta("ui", JsonValue = """{"visibility":["app"]}""")]
    [Description("Change one pending draft row. Send the row's revision; a changed title clears the game unless it is reselected.")]
    public async Task<CallToolResult> UpdateDraft(ClaimsPrincipal user, Guid batchId, Guid rowId, int revision,
        string title, string confirmToken, string? date = null, int? durationMinutes = null, string platform = "unknown",
        string? gameId = null, string? igdbId = null, CancellationToken ct = default) => await Run(async () =>
        {
            var userId = Confirmed(user, batchId, confirmToken);
            var row = await drafts.Update(userId, batchId, rowId,
                new DraftUpdateRequest(revision, title, date, durationMinutes, platform, gameId, igdbId), ct);
            return Structured(new { row });
        });

    [McpServerTool(Name = "delete_draft", Title = "Delete a draft", Destructive = true, Idempotent = false, OpenWorld = false)]
    [McpMeta("ui", JsonValue = """{"visibility":["app"]}""")]
    [Description("Drop one pending draft row.")]
    public async Task<CallToolResult> DeleteDraft(ClaimsPrincipal user, Guid batchId, Guid rowId, int revision,
        string confirmToken, CancellationToken ct) => await Run(async () =>
        {
            await drafts.Delete(Confirmed(user, batchId, confirmToken), batchId, rowId, revision, ct);
            return Structured(new { deleted = rowId });
        });

    [McpServerTool(Name = "accept_drafts", Title = "Accept drafts", Destructive = false, Idempotent = true, OpenWorld = false)]
    [McpMeta("ui", JsonValue = """{"visibility":["app"]}""")]
    [Description("Save draft rows as play sessions. Accepting a row again returns the session it became.")]
    public async Task<CallToolResult> AcceptDrafts(ClaimsPrincipal user, Guid batchId, DraftAcceptRow[] rows,
        string confirmToken, CancellationToken ct) => await Run(async () =>
        {
            var response = await drafts.Accept(Confirmed(user, batchId, confirmToken), batchId,
                new DraftAcceptRequest(rows ?? []), ct);
            return Structured(response);
        });

    private long Confirmed(ClaimsPrincipal user, Guid batchId, string? token)
    {
        var userId = user.GetUserId();
        // The same answer as for a batch that does not exist: a token is only ever issued for the user's own batch.
        return confirmation.IsValid(userId, batchId, token) ? userId : throw AppProblemException.NotFound("Draft batch not found.");
    }

    private static CallToolResult Structured(object value)
    {
        var element = JsonSerializer.SerializeToElement(value, _json);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = element.GetRawText() }],
            StructuredContent = element,
        };
    }

    /// <summary>Validation and conflict messages reach the caller; anything else stays the SDK's generic error.</summary>
    private static async Task<CallToolResult> Run(Func<Task<CallToolResult>> action)
    {
        try
        {
            return await action();
        }
        catch (AppProblemException problem)
        {
            return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = problem.Message }] };
        }
    }

    private static string Summary(DraftBatchDto batch)
    {
        var text = new StringBuilder().Append(CultureInfo.InvariantCulture,
            $"Drafted {batch.Rows.Count} session(s) for review. Nothing is logged until the user accepts them in the card or in Playdex's pending drafts.");
        foreach (var row in batch.Rows)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"\n- {row.Selection?.Title ?? row.Title}: {row.Date ?? "no date"}, {row.DurationMinutes?.ToString(CultureInfo.InvariantCulture) ?? "?"} min, {row.PlatformKey}");
            if (row.Warnings.Count > 0)
            {
                text.Append(" (").AppendJoin(" ", row.Warnings).Append(')');
            }
        }

        return text.ToString();
    }
}
