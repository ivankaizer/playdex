import { requireAuth } from "../../app/http/auth";
import { ok } from "../../app/http/responses";
import type {
  ApiResponse,
  RecapResponseDto,
  RecapYearsResponseDto,
} from "../../app/http/types";
import { AppError } from "../../common/errors/AppError";
import { encodeGameId } from "../../common/utils/gameHash";
import type { RecapStats } from "./recap.domain";
import { recapService } from "./recap.service";

const parseRecapRange = (url: URL) => {
  const fromRaw = url.searchParams.get("from");
  const toRaw = url.searchParams.get("to");

  if (!fromRaw && !toRaw) {
    return null;
  }

  if (!fromRaw || !toRaw) {
    throw new AppError("from and to are required.", 400);
  }

  const parsedFrom = new Date(fromRaw);
  const parsedTo = new Date(toRaw);

  if (
    Number.isNaN(parsedFrom.getTime()) ||
    Number.isNaN(parsedTo.getTime())
  ) {
    throw new AppError("from and to must be valid dates.", 400);
  }

  if (parsedFrom > parsedTo) {
    throw new AppError("from must be before to.", 400);
  }

  const endedAt = new Date(
    Date.UTC(
      parsedTo.getUTCFullYear(),
      parsedTo.getUTCMonth(),
      parsedTo.getUTCDate() + 1
    )
  );

  return {
    from: fromRaw,
    to: toRaw,
    startedAt: parsedFrom,
    endedAt,
  };
};

const toRecapDto = (stats: RecapStats): RecapResponseDto => ({
  ...stats,
  topGameBySessions: stats.topGameBySessions
    ? {
        ...stats.topGameBySessions,
        gameId: encodeGameId(stats.topGameBySessions.gameId),
      }
    : null,
  topGameByTime: stats.topGameByTime
    ? { ...stats.topGameByTime, gameId: encodeGameId(stats.topGameByTime.gameId) }
    : null,
  topGamesByTime: stats.topGamesByTime.map((game) => ({
    ...game,
    gameId: encodeGameId(game.gameId),
  })),
  topGameByAverageSession: stats.topGameByAverageSession
    ? {
        ...stats.topGameByAverageSession,
        gameId: encodeGameId(stats.topGameByAverageSession.gameId),
      }
    : null,
  gameWithLongestSession: stats.gameWithLongestSession
    ? {
        ...stats.gameWithLongestSession,
        gameId: encodeGameId(stats.gameWithLongestSession.gameId),
      }
    : null,
  mostBingedGame: stats.mostBingedGame
    ? {
        ...stats.mostBingedGame,
        gameId: encodeGameId(stats.mostBingedGame.gameId),
      }
    : null,
  gamesTable: stats.gamesTable.map((game) => ({
    ...game,
    gameId: encodeGameId(game.gameId),
  })),
});

export const getRecapYearsHandler = async (ctx: {
  request: Request;
}): Promise<ApiResponse<RecapYearsResponseDto>> => {
  const auth = requireAuth(ctx.request);
  const years = await recapService.fetchRecapYears(auth.userId);
  return ok({ years });
};

export const getRecapHandler = async (ctx: {
  request: Request;
}): Promise<ApiResponse<RecapResponseDto>> => {
  const auth = requireAuth(ctx.request);
  const range = parseRecapRange(new URL(ctx.request.url));
  const stats = await recapService.fetchRecap({
    userId: auth.userId,
    from: range?.from ?? null,
    to: range?.to ?? null,
    startedAt: range?.startedAt ?? null,
    endedAt: range?.endedAt ?? null,
  });
  return ok(toRecapDto(stats));
};
