import type { AccountSession } from "../types";

export const getSessionDurationSeconds = (session: AccountSession) => {
  const started = new Date(session.startedAt).getTime();
  const ended = session.endedAt
    ? new Date(session.endedAt).getTime()
    : Date.now();
  return Math.max(0, Math.floor((ended - started) / 1000));
};
