using Api.Infrastructure.Common;
using Api.Infrastructure.Http;
using Api.Modules.Games;
using Api.Modules.Manual;
using Api.Modules.Manual.Drafts;
using Api.Modules.Manual.Drafts.Extraction;
using Api.Modules.Polling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Playdex.Persistence;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class DraftServiceTests(PostgresFixture postgres)
{
    [Fact]
    public async Task AcceptanceIsExplicitAndRetrySafeEvenAfterSessionDeletion()
    {
        if (!postgres.IsConfigured) return;
        await using var db = CreateDb();
        var (user, game, batch) = await Seed(db);
        var service = Service(db);
        Assert.Equal(0, await db.PlaySessions.CountAsync(s => s.UserId == user.Id));
        var row = batch.Rows[0];
        var request = new DraftAcceptRequest([new(row.Id, 1)]);
        var first = Assert.Single((await service.Accept(user.Id, batch.Id, request, default)).Results);
        Assert.Null(first.Error);
        Assert.NotNull(first.SessionId);
        var session = await db.PlaySessions.SingleAsync(s => s.Id == first.SessionId);
        Assert.Equal(CaptureMethod.Manual, session.CaptureMethod);
        Assert.Null(session.IntegrationId);
        Assert.Equal(game.Id, session.GameId);
        Assert.Equal(TimeSpan.FromMinutes(90), session.EndedAt - session.StartedAt);
        Assert.Equal(new DateTimeOffset(2026,9,9,0,0,0,TimeSpan.Zero), session.StartedAt);
        db.Remove(session); await db.SaveChangesAsync();
        var retry = Assert.Single((await service.Accept(user.Id, batch.Id, request, default)).Results);
        Assert.Equal(first.SessionId, retry.SessionId);
        Assert.Empty(await db.PlaySessions.Where(s => s.UserId == user.Id).ToListAsync());
    }

    [Fact]
    public async Task StaleAndInvalidRowsFailWithoutRollingBackValidRows()
    {
        if (!postgres.IsConfigured) return;
        await using var db = CreateDb();
        var (user, _, batch) = await Seed(db);
        var invalid = new SessionDraftRow { Id=Guid.NewGuid(),BatchId=batch.Id,Position=1,Title="Missing date",PlatformKey="unknown" };
        db.SessionDraftRows.Add(invalid); await db.SaveChangesAsync();
        var response = await Service(db).Accept(user.Id,batch.Id,new([new(invalid.Id,1),new(batch.Rows[0].Id,1)]),default);
        Assert.NotNull(response.Results[0].Error);
        Assert.NotNull(response.Results[1].SessionId);
        Assert.Equal(1,await db.PlaySessions.CountAsync(s=>s.UserId==user.Id));
    }

    [Fact]
    public async Task EditingTitleClearsSelectionAndOwnerAndRevisionAreEnforced()
    {
        if (!postgres.IsConfigured) return;
        await using var db = CreateDb();
        var (user, game, batch) = await Seed(db);
        var service = Service(db);
        var row=batch.Rows[0];
        var update=new DraftUpdateRequest(1,"Different game","2026-09-09",90,"unknown",GameId.Encode(game.Id),null);
        var foreign=await Assert.ThrowsAsync<AppProblemException>(()=>service.Update(user.Id+1000000,batch.Id,row.Id,update,default));
        Assert.Equal(404,foreign.StatusCode);
        var changed=await service.Update(user.Id,batch.Id,row.Id,update,default);
        Assert.Null(changed.Selection);
        Assert.False(changed.Ready);
        Assert.Equal(2,changed.Revision);
        var stale=await Assert.ThrowsAsync<AppProblemException>(()=>service.Update(user.Id,batch.Id,row.Id,update,default));
        Assert.Equal(409,stale.StatusCode);
        await service.Delete(user.Id,batch.Id,row.Id,2,default);
        var result=Assert.Single((await service.Accept(user.Id,batch.Id,new([new(row.Id,2)]),default)).Results);
        Assert.NotNull(result.Error);
        Assert.Empty(await db.PlaySessions.Where(s=>s.UserId==user.Id).ToListAsync());
    }

    private DraftService Service(PlaydexDbContext db)
    {
        var opts=Options.Create(new IgdbOptions());
        var manual=new ManualService(new(db),new IgdbClient(new HttpClient(),opts),opts,new GameResolver(db));
        return new DraftService(new(db),db,manual,new(db));
    }
    private static async Task<(User,Game,SessionDraftBatch)> Seed(PlaydexDbContext db)
    {
        var now=DateTimeOffset.UtcNow;
        var user=new User {Email=$"draft-{Guid.NewGuid():N}@test.local",CreatedAt=now,UpdatedAt=now};
        var game=new Game {Title="Review game",CreatedAt=now,UpdatedAt=now};
        db.AddRange(user,game); await db.SaveChangesAsync();
        var batch=new SessionDraftBatch {Id=Guid.NewGuid(),UserId=user.Id,RequestId=Guid.NewGuid(),CreatedAt=now,Timezone="UTC",ReferenceDate=new(2026,9,9)};
        batch.Rows.Add(new SessionDraftRow {Id=Guid.NewGuid(),BatchId=batch.Id,Position=0,Title=game.Title,Date="2026-09-09",DurationMinutes=90,PlatformKey="unknown",GameId=GameId.Encode(game.Id),SelectionJson=System.Text.Json.JsonSerializer.Serialize(new ManualMappingSearchResultDto("local",GameId.Encode(game.Id),null,game.Title,null,null,null))});
        await new DraftRepository(db).CreateBatch(batch,default);
        return(user,game,batch);
    }
    private PlaydexDbContext CreateDb()=>new(new DbContextOptionsBuilder<PlaydexDbContext>().UseNpgsql(postgres.DataSource!,n=>{n.MapEnum<GameExternalIdentityType>("game_external_identity_type");n.MapEnum<IntegrationType>("integration_type");n.MapEnum<CaptureMethod>("capture_method");}).Options);
}
