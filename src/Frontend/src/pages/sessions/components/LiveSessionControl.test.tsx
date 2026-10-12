import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { LiveSessionControl, liveTimerView } from "./LiveSessionControl";

describe("liveTimerView", () => {
  const start = new Date(2026, 9, 9, 18, 5, 0);
  const at = (seconds: number) => start.getTime() + seconds * 1000;

  it("ticks as H:MM:SS under a day without a stale hint", () => {
    expect(liveTimerView(start.toISOString(), at(23 * 60 + 12))).toEqual({ timer: "0:23:12", since: "18:05", staleFor: "" });
  });

  it("asks whether the session is still running from 12 hours", () => {
    expect(liveTimerView(start.toISOString(), at(12 * 3600)).staleFor).toBe("over 12 hours");
  });

  it("shows days and the start weekday past a day", () => {
    const view = liveTimerView(start.toISOString(), at(74 * 3600 + 20 * 60 + 34));
    expect(view.timer).toBe("3d 2h 20m");
    expect(view.since).toBe("Fri 18:05");
    expect(view.staleFor).toBe("over 74 hours");
  });
});

describe("LiveSessionControl live row", () => {
  it("puts platform and start on their own line and warns about a forgotten session", () => {
    const title = "Warhammer 40,000: Dawn of War – Definitive Edition";
    render(
      <LiveSessionControl
        activeSession={{
          sessionId: 1,
          gameId: "9",
          title,
          platformKey: "switch2",
          platformName: "Nintendo Switch 2",
          startedAt: new Date(Date.now() - 13 * 3600_000).toISOString(),
          capsuleUrl: null,
        }}
        searchGames={vi.fn()}
        onStart={() => undefined}
        onStop={() => undefined}
      />,
    );
    expect(screen.getByTitle(title)).toHaveTextContent(title);
    expect(screen.getByText(/^Nintendo Switch 2 · since \d\d:\d\d$/)).toBeInTheDocument();
    expect(screen.getByText("Still playing? This session has been running for over 13 hours.")).toBeInTheDocument();
  });
});
