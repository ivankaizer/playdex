using Microsoft.EntityFrameworkCore;
using Api.Infrastructure.Http;
using Api.Infrastructure.Persistence;

namespace Api.Modules.Games;

public sealed class GameMergeService(PlaydexDbContext db)
{
    public async Task Merge(long sourceGameId, long targetGameId, CancellationToken ct)
    {
        if (sourceGameId == targetGameId)
            throw AppProblemException.Validation("Source and target Games must differ.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var locked = await db.Games
            .FromSqlInterpolated($"select * from games where id in ({sourceGameId}, {targetGameId}) for update")
            .OrderBy(row => row.Id).ToListAsync(ct);
        var source = locked.SingleOrDefault(row => row.Id == sourceGameId)
            ?? throw AppProblemException.NotFound("Source Game not found.");
        var target = locked.SingleOrDefault(row => row.Id == targetGameId)
            ?? throw AppProblemException.NotFound("Target Game not found.");

        if (!target.TitleLocked && source.TitleLocked) target.Title = source.Title;
        if (!target.CapsuleUrlLocked && target.CapsuleUrl is null) target.CapsuleUrl = source.CapsuleUrl;
        target.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await db.Database.ExecuteSqlInterpolatedAsync($"update game_external_identities set game_id={targetGameId}, updated_at=now() where game_id={sourceGameId}", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"update play_sessions set game_id={targetGameId}, updated_at=now() where game_id={sourceGameId}", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"update poll_results set game_id={targetGameId} where game_id={sourceGameId}", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"update manual_game_inputs set mapped_game_id={targetGameId}, updated_at=now() where mapped_game_id={sourceGameId}", ct);

        await db.Database.ExecuteSqlInterpolatedAsync($"delete from ignored_games s where s.game_id={sourceGameId} and exists(select 1 from ignored_games t where t.user_id=s.user_id and t.game_id={targetGameId})", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"update ignored_games set game_id={targetGameId}, updated_at=now() where game_id={sourceGameId}", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"delete from game_ratings s where s.game_id={sourceGameId} and exists(select 1 from game_ratings t where t.user_id=s.user_id and t.game_id={targetGameId})", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"update game_ratings set game_id={targetGameId}, updated_at=now() where game_id={sourceGameId}", ct);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            with neighbors as (
                select user_id, case when game_id={sourceGameId} then connected_game_id else game_id end neighbor
                from game_connections where game_id={sourceGameId} or connected_game_id={sourceGameId}
            ), removed as (
                delete from game_connections where game_id={sourceGameId} or connected_game_id={sourceGameId}
            )
            insert into game_connections(user_id,game_id,connected_game_id,created_at,updated_at)
            select user_id,least({targetGameId},neighbor),greatest({targetGameId},neighbor),now(),now()
            from neighbors where neighbor<>{targetGameId}
            on conflict do nothing
            """, ct);

        db.Games.Remove(source);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
