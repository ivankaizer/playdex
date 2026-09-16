using Api.Modules.Manual.Drafts;
using Api.Modules.Manual.Drafts.Extraction;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Playdex.Persistence;
using Playdex.Persistence.Migrations;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class GenerationHistoryRecorderTests(PostgresFixture postgres)
{
    [Fact]
    public async Task RetainsEvidenceAndFailedAttemptsAndExpiresOnlyOldHistory()
    {
        if (!postgres.IsConfigured) return;
        await using var connection = await postgres.DataSource!.OpenConnectionAsync();
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await new AiGenerationHistoryMigration().ApplyAsync(connection, transaction, default);
            await transaction.CommitAsync();
        }
        await using var db = new PlaydexDbContext(new DbContextOptionsBuilder<PlaydexDbContext>().UseNpgsql(postgres.DataSource).Options);
        var now = DateTimeOffset.UtcNow;
        var user = new User {Email=$"history-{Guid.NewGuid():N}@test.local",CreatedAt=now,UpdatedAt=now};
        db.Users.Add(user);
        await db.SaveChangesAsync();
        try
        {
            var options = Options.Create(new AiDraftOptions { Provider="test",Model="test",HistoryRetentionDays=30 });
            var recorder = new GenerationHistoryRecorder(postgres.DataSource,options,TimeProvider.System,NullLogger<GenerationHistoryRecorder>.Instance);
            var request = Guid.NewGuid();
            var id = await recorder.Begin(user.Id,request,"UTC",new("Hades for an hour",[1,2,3],"image/png",new(2026,9,16),["pc"]));
            Assert.NotNull(id);
            await recorder.RecordResponse(id,new(200,"not json",false));
            await recorder.RecordResponse(id,new(429,"provider error",false));
            await recorder.Complete(id,"failure","AI extraction failed.");
            var record = await db.AiGenerations.AsNoTracking().SingleAsync(row=>row.Id==id);
            Assert.Equal(request,record.RequestId);
            Assert.Equal("Hades for an hour",record.InputText);
            Assert.Equal(new byte[]{1,2,3},record.Image);
            Assert.Contains("not json",record.ResponsesJson);
            Assert.Contains("provider error",record.ResponsesJson);
            Assert.Equal("failure",record.Status);
            Assert.NotNull(record.CompletedAt);
            await recorder.DeleteExpired(default);
            Assert.True(await db.AiGenerations.AnyAsync(row=>row.Id==id));
            await db.AiGenerations.Where(row=>row.Id==id).ExecuteUpdateAsync(setters=>setters.SetProperty(row=>row.CreatedAt,now.AddDays(-31)));
            options.Value.HistoryRetentionDays=0;
            Assert.Equal(0,await recorder.DeleteExpired(default));
            Assert.True(await db.AiGenerations.AnyAsync(row=>row.Id==id));
            options.Value.HistoryRetentionDays=30;
            await recorder.DeleteExpired(default);
            Assert.False(await db.AiGenerations.AnyAsync(row=>row.Id==id));
        }
        finally { await db.Users.Where(row=>row.Id==user.Id).ExecuteDeleteAsync(); }
    }
}
