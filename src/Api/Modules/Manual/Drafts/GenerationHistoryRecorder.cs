using System.Text.Json;
using Api.Modules.Manual.Drafts.Extraction;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Api.Modules.Manual.Drafts;

public sealed partial class GenerationHistoryRecorder(NpgsqlDataSource dataSource,
    IOptions<AiDraftOptions> options, TimeProvider clock, ILogger<GenerationHistoryRecorder> logger)
{
    public async Task<Guid?> Begin(long userId, Guid requestId, string timezone, ExtractionInput input)
    {
        var id = Guid.NewGuid();
        await using var command = dataSource.CreateCommand("""
            insert into ai_generations(id,user_id,request_id,created_at,provider,model,timezone,reference_date,input_text,image,image_content_type)
            values (@id,@user,@request,@created,@provider,@model,@timezone,@date,@text,@image,@type)
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("request", requestId);
        command.Parameters.AddWithValue("created", clock.GetUtcNow());
        command.Parameters.AddWithValue("provider", options.Value.Provider);
        command.Parameters.AddWithValue("model", options.Value.Model!);
        command.Parameters.AddWithValue("timezone", timezone);
        command.Parameters.AddWithValue("date", input.ReferenceDate);
        command.Parameters.AddWithValue("text", NpgsqlDbType.Text, (object?)input.Text ?? DBNull.Value);
        command.Parameters.AddWithValue("image", NpgsqlDbType.Bytea, (object?)input.Image ?? DBNull.Value);
        command.Parameters.AddWithValue("type", NpgsqlDbType.Text, (object?)input.ImageContentType?.ToLowerInvariant() ?? DBNull.Value);
        return await TryWrite(command) ? id : null;
    }

    public async Task RecordResponse(Guid? id, ExtractionResponse response)
    {
        if (id is null) return;
        await using var command = dataSource.CreateCommand("""
            update ai_generations set responses_json=responses_json || @response where id=@id
            """);
        command.Parameters.AddWithValue("id", id.Value);
        command.Parameters.AddWithValue("response", NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(new[] { new { response.HttpStatus, response.Body, response.Truncated, ReceivedAt = clock.GetUtcNow() } }));
        await TryWrite(command);
    }

    public async Task Complete(Guid? id, string status, string? error = null, DraftBatchDto? result = null)
    {
        if (id is null) return;
        await using var command = dataSource.CreateCommand("""
            update ai_generations set completed_at=@completed,status=@status,error=@error,result_json=@result where id=@id
            """);
        command.Parameters.AddWithValue("id", id.Value);
        command.Parameters.AddWithValue("completed", clock.GetUtcNow());
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("error", NpgsqlDbType.Text, (object?)error ?? DBNull.Value);
        command.Parameters.AddWithValue("result", NpgsqlDbType.Jsonb,
            result is null ? DBNull.Value : JsonSerializer.Serialize(result));
        await TryWrite(command);
    }

    public async Task<int> DeleteExpired(CancellationToken ct)
    {
        var days = options.Value.HistoryRetentionDays;
        if (days <= 0) return 0;
        await using var command = dataSource.CreateCommand("delete from ai_generations where created_at < @cutoff");
        command.Parameters.AddWithValue("cutoff", clock.GetUtcNow().AddDays(-days));
        return await command.ExecuteNonQueryAsync(ct);
    }

    private async Task<bool> TryWrite(NpgsqlCommand command)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await command.ExecuteNonQueryAsync(timeout.Token);
            return true;
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException or OperationCanceledException)
        {
            LogWriteFailed();
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI generation history write failed; extraction continues without complete history.")]
    private partial void LogWriteFailed();
}

public sealed partial class GenerationHistoryCleanup(GenerationHistoryRecorder history,
    ILogger<GenerationHistoryCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try { await history.DeleteExpired(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) when (exception is NpgsqlException or TimeoutException)
            { LogCleanupFailed(); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI generation history cleanup failed; will retry next hour.")]
    private partial void LogCleanupFailed();
}
