import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { useState } from "react";
import type { DraftBatchDto, DraftCapabilities, DraftRowDto } from "@/models/drafts";
import type { ManualMappingSearchResult, ManualSessionStartRequest } from "@/models/manual";
import { openSessionCreate, completeSearch, type SessionCreateState } from "../sessionCreateState";
import { dataUrlBytes, readAsDataUrl } from "./draftInterop";
import { DraftWorkspace } from "./DraftWorkspace";
import { LiveSessionControl } from "./LiveSessionControl";
import { SessionActions } from "./SessionActions";
import { SessionCreateModal } from "./SessionCreateModal";
import { SessionEditModal } from "./SessionEditModal";

vi.mock("./draftInterop", async (importOriginal) => ({
  ...(await importOriginal<typeof import("./draftInterop")>()),
  validateImage: async () => true,
  draftTimezone: () => "Europe/Warsaw",
}));

function json(data: unknown, status = 200) {
  return new Response(JSON.stringify({ data, error: null }), { status, headers: { "Content-Type": "application/json" } });
}

function problem(detail: string, status: number) {
  return new Response(JSON.stringify({ detail }), { status, headers: { "Content-Type": "application/json" } });
}

const buttonByText = (text: string) =>
  screen.getAllByRole("button").find((button) => button.textContent?.trim() === text) as HTMLButtonElement;

describe("SessionActions", () => {
  it("offers edit and remove for finished sessions", () => {
    const { container } = render(<SessionActions canEdit />);
    expect(container.querySelector("button[data-action=edit]")?.textContent).toBe("Edit");
    expect(container.querySelector("button[data-action=remove]")?.textContent).toBe("Remove");
  });

  it("does not offer edit for active sessions", () => {
    const { container } = render(<SessionActions canEdit={false} />);
    expect(container.querySelectorAll("button[data-action=edit]")).toHaveLength(0);
    expect(container.querySelectorAll("button[data-action=remove]")).toHaveLength(1);
  });
});

describe("SessionEditModal", () => {
  it("renders an accessible second-precision date time editor", () => {
    const { container } = render(
      <SessionEditModal gameTitle="Hades II" startedAt="2026-08-04T18:30:15" endedAt="2026-08-04T20:45:30" />,
    );
    const dialog = screen.getByRole("dialog");
    expect(dialog).toHaveAttribute("aria-modal", "true");
    expect(dialog).toHaveTextContent("Hades II");
    const inputs = container.querySelectorAll<HTMLInputElement>("input[type=datetime-local]");
    expect(inputs).toHaveLength(2);
    inputs.forEach((input) => expect(input).toHaveAttribute("step", "1"));
    expect(inputs[0].getAttribute("value")).toBe("2026-08-04T18:30:15");
    expect(inputs[1].getAttribute("value")).toBe("2026-08-04T20:45:30");
    expect(container.querySelector("button[data-action=cancel]")?.textContent).toBe("Cancel");
    expect(container.querySelector("button[data-action=save]")?.textContent).toBe("Save changes");
  });
});

function CreateModalHarness({
  initial,
  aiAvailable,
  embedded,
  onUseAi,
}: {
  initial: SessionCreateState;
  aiAvailable?: boolean;
  embedded?: boolean;
  onUseAi?: () => void;
}) {
  const [state, setState] = useState(initial);
  return (
    <SessionCreateModal
      state={state}
      onStateChange={setState}
      aiAvailable={aiAvailable}
      embedded={embedded}
      onUseAi={onUseAi}
      platforms={[
        { key: "pc", name: "PC" },
        { key: "ps5", name: "PS5" },
      ]}
    />
  );
}

describe("SessionCreateModal", () => {
  it("AI entry requires historical mode and the capability", () => {
    const onUseAi = vi.fn();
    const { container, rerender } = render(
      <CreateModalHarness initial={openSessionCreate("historical", new Date())} aiAvailable onUseAi={onUseAi} />,
    );
    fireEvent.click(buttonByText("Use AI"));
    expect(onUseAi).toHaveBeenCalled();
    fireEvent.click(container.querySelector("[data-mode=live]")!);
    expect(container.innerHTML).not.toContain("Use AI");
    fireEvent.click(container.querySelector("[data-mode=historical]")!);
    expect(container.innerHTML).toContain("Use AI");
    rerender(<CreateModalHarness initial={openSessionCreate("historical", new Date())} aiAvailable={false} onUseAi={onUseAi} />);
    expect(container.innerHTML).not.toContain("Use AI");
  });

  it("renders the modal with search, results and platform controls", () => {
    const state = completeSearch(openSessionCreate("live", new Date()), {
      query: "Hades",
      igdbAvailable: true,
      results: [{ source: "igdb", gameId: null, igdbId: "114283", title: "Hades II", platform: "PC", platformId: 6, capsuleUrl: null }],
    });
    const { container } = render(<CreateModalHarness initial={state} />);
    expect(screen.getByRole("dialog")).toHaveAttribute("aria-modal", "true");
    expect(container.querySelectorAll("button[data-mode]")).toHaveLength(2);
    expect(container.querySelector("input[type=search]")).not.toBeNull();
    expect(container.querySelectorAll("select[data-field=platform] option")).toHaveLength(3);
    expect(container.innerHTML).toContain("IGDB");
  });

  it("embedded historical creator uses page sections without modal controls", () => {
    const { container } = render(<CreateModalHarness initial={openSessionCreate("historical", new Date())} embedded aiAvailable />);
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(container.querySelectorAll("button[data-mode]")).toHaveLength(0);
    expect(container.innerHTML).not.toContain("Close session creator");
    expect(container.innerHTML).not.toContain(">Cancel<");
    expect(container.innerHTML).toContain("Choose a game");
    expect(container.innerHTML).toContain("Session details");
    expect(container.querySelector("[data-layout=extended-manual]")).not.toBeNull();
  });

  it("a platform without a matching option shows no selection like Blazor", () => {
    const state = { ...openSessionCreate("historical", new Date()), platformKey: "switch" };
    const { container } = render(<CreateModalHarness initial={state} embedded />);
    expect(container.querySelector<HTMLSelectElement>("select[data-field=platform]")!.selectedIndex).toBe(-1);
  });
});

describe("LiveSessionControl", () => {
  it("search shows the poster and start requires a selection and platform", async () => {
    let started: ManualSessionStartRequest | null = null;
    const result: ManualMappingSearchResult = {
      source: "igdb",
      gameId: null,
      igdbId: "114283",
      title: "Hades",
      platform: "PC",
      platformId: 6,
      capsuleUrl: "https://covers.test/hades.png",
    };
    const searchGames = vi.fn(async () => ({ query: "Hades", igdbAvailable: true, results: [result] }));
    const { container } = render(
      <LiveSessionControl
        platforms={[{ key: "pc", name: "PC" }]}
        searchGames={searchGames}
        onStart={(request) => {
          started = request;
        }}
        onStop={() => undefined}
      />,
    );

    const platform = container.querySelector("[data-live-platform]")!;
    expect(platform.classList).toContain("sm:w-36");
    expect(buttonByText("Start live session")).toBeDisabled();
    fireEvent.change(container.querySelector("input[type=search]")!, { target: { value: "Hades" } });
    fireEvent.click(buttonByText("Search"));
    await waitFor(() => expect(container.innerHTML).toContain("hades.png"));
    expect(searchGames).toHaveBeenCalledWith("/manual/search?query=Hades", expect.any(AbortSignal));

    fireEvent.click(screen.getAllByRole("button").find((button) => button.textContent?.includes("Hades"))!);
    fireEvent.change(container.querySelector("select")!, { target: { value: "pc" } });
    const start = buttonByText("Start live session");
    expect(start).not.toBeDisabled();
    fireEvent.click(start);
    expect(started).toMatchObject({ igdbId: "114283", platformKey: "pc", title: "Hades" });
  });
});

describe("DraftWorkspace", () => {
  const capabilities = (overrides: Partial<DraftCapabilities> = {}): DraftCapabilities => ({
    available: true,
    provider: "OpenRouter",
    maxTextCharacters: 10000,
    maxImageBytes: 10 * 1024 * 1024,
    maxImagePixels: 20_000_000,
    maxRows: 50,
    ...overrides,
  });

  const draftRow = (overrides: Partial<DraftRowDto> = {}): DraftRowDto => ({
    id: crypto.randomUUID(),
    revision: 1,
    status: "pending",
    title: "Hades",
    date: "2026-09-08",
    durationMinutes: 60,
    platformKey: "unknown",
    selection: { source: "local", gameId: "game", igdbId: null, title: "Hades", platform: null, platformId: null, capsuleUrl: null },
    warnings: [],
    ready: true,
    acceptedSessionId: null,
    ...overrides,
  });

  const batchOf = (...rows: DraftRowDto[]): DraftBatchDto => ({ id: crypto.randomUUID(), createdAt: new Date().toISOString(), rows });

  type Handler = (url: URL, init: RequestInit) => Response | Promise<Response>;
  function stubFetch(handler: Handler) {
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => handler(new URL(String(input)), init ?? {}));
    vi.stubGlobal("fetch", fetchMock);
    return fetchMock;
  }

  /** Serves the batch list and the single batch; other requests go to `other`. */
  const batchServer =
    (batch: DraftBatchDto, other?: Handler): Handler =>
    (url, init) => {
      const method = init.method ?? "GET";
      if (method === "GET" && url.pathname.endsWith(batch.id)) return json(batch);
      if (method === "GET" && url.pathname.endsWith("/manual/draft-batches")) return json([batch]);
      return other ? other(url, init) : json(null);
    };

  async function resume() {
    await waitFor(() => expect(screen.getAllByRole("button").some((b) => b.textContent?.startsWith("Resume"))).toBe(true));
    fireEvent.click(screen.getAllByRole("button").find((b) => b.textContent?.startsWith("Resume"))!);
  }

  async function generationParts(init: RequestInit) {
    const form = init.body as FormData;
    const file = form.get("file");
    return {
      id: String(form.get("requestId")),
      text: String(form.get("text")),
      image: file instanceof Blob ? Array.from(dataUrlBytes(await readAsDataUrl(file))) : null,
    };
  }

  async function addScreenshot(container: HTMLElement) {
    const input = container.querySelector<HTMLInputElement>("input[type=file]")!;
    const file = new File([new Uint8Array([1, 2, 3])], "shot.png", { type: "image/png" });
    fireEvent.change(input, { target: { files: [file] } });
    await waitFor(() => expect(screen.getByAltText("Screenshot to be sent for extraction")).toBeInTheDocument());
  }

  afterEach(() => vi.unstubAllGlobals());

  it.each([
    [30, "for 30 days"],
    [0, "until deleted"],
  ])("discloses administrator retention of %s days", async (days, period) => {
    stubFetch(() => json([]));
    const { container } = render(<DraftWorkspace embedded capabilities={capabilities({ historyRetentionDays: days })} />);
    await waitFor(() => expect(container.innerHTML).toContain("No saved drafts"));
    expect(container.innerHTML).toContain("Playdex retains your input, screenshot, and AI results");
    expect(container.innerHTML).toContain(period);
    expect(container.innerHTML).toContain("administrator review");
    expect(container.innerHTML).toContain("up to 10 MiB and 20 megapixels.");
  });

  it("resumes when generation is disabled and missing fields block accept", async () => {
    const batch = batchOf(draftRow({ date: null, durationMinutes: null, selection: null, ready: false, warnings: ["Platform is Unknown."] }));
    stubFetch(batchServer(batch));
    const { container } = render(<DraftWorkspace />);
    await resume();
    await waitFor(() => expect(container.querySelectorAll("input[type=date]")).toHaveLength(1));
    expect(screen.getByRole("navigation", { name: "Breadcrumb" })).toHaveTextContent("Saved batches");
    expect(container.querySelector("input[data-game-search]")).toHaveAttribute("aria-invalid", "true");
    expect(container.querySelector("input[type=date]")).toHaveAttribute("aria-invalid", "true");
    container.querySelectorAll("input[type=number]").forEach((input) => expect(input).toHaveAttribute("aria-invalid", "true"));
    expect(container.querySelector("input[type=date]")!.classList).toContain("border-rose-500");
    expect(container.querySelector("select")!.classList).toContain("border-rose-500");
    expect(container.querySelector("select")).toHaveAttribute("data-needs-review", "");
    expect(container.innerHTML).not.toContain("Platform is Unknown");
    expect(buttonByText("Accept")).toBeDisabled();
    expect(container.querySelector("fieldset")).toHaveAttribute("data-dw-scope");
  });

  it("a failed save keeps edits and does not accept", async () => {
    const batch = batchOf(draftRow());
    stubFetch(batchServer(batch, (_url, init) => (init.method === "PATCH" ? problem("Stale revision", 409) : json(null))));
    const { container } = render(<DraftWorkspace />);
    await resume();
    await waitFor(() => expect(container.querySelectorAll("input[type=date]")).toHaveLength(1));
    fireEvent.change(container.querySelectorAll("input[type=number]")[0], { target: { value: "3" } });
    fireEvent.click(buttonByText("Save edits"));
    await waitFor(() => expect(container.innerHTML).toContain("Stale revision Your edits are still here."));
    expect(container.querySelectorAll<HTMLInputElement>("input[type=number]")[0].value).toBe("3");
    expect(container.innerHTML).not.toContain("Accepted — session saved");
  });

  it("partial acceptance marks only successful rows and notifies", async () => {
    const first = draftRow();
    const second = draftRow();
    const batch = batchOf(first, second);
    const accepted = vi.fn();
    let acceptBody: unknown = null;
    stubFetch(
      batchServer(batch, (_url, init) => {
        acceptBody = JSON.parse(String(init.body));
        return json({ results: [{ id: first.id, sessionId: 42, error: null }, { id: second.id, sessionId: null, error: "IGDB unavailable" }] });
      }),
    );
    const { container } = render(<DraftWorkspace onAccepted={accepted} />);
    await resume();
    await waitFor(() => expect(container.querySelectorAll("article")).toHaveLength(2));
    fireEvent.click(buttonByText("Accept all ready"));
    await waitFor(() => expect(container.innerHTML).toContain("IGDB unavailable"));
    expect(acceptBody).toEqual({ rows: [{ id: first.id, revision: 1 }, { id: second.id, revision: 1 }] });
    expect(accepted).toHaveBeenCalled();
    const fieldsets = container.querySelectorAll("fieldset");
    expect(fieldsets[0]).toBeDisabled();
    expect(fieldsets[1]).not.toBeDisabled();
  });

  it("completing the last row removes the batch from saved review", async () => {
    const row = draftRow();
    const batch = batchOf(row);
    let listCalls = 0;
    stubFetch((url, init) => {
      if (init.method === "POST") return json({ results: [{ id: row.id, sessionId: 42, error: null }] });
      if (url.pathname.endsWith(batch.id)) return json(batch);
      listCalls++;
      return json(listCalls === 1 ? [batch] : []);
    });
    const accepted = vi.fn();
    const { container } = render(<DraftWorkspace onAccepted={accepted} />);
    await resume();
    await waitFor(() => expect(container.querySelectorAll("article")).toHaveLength(1));
    fireEvent.click(buttonByText("Accept all ready"));
    await waitFor(() => expect(container.innerHTML).toContain("removed from saved review"));
    expect(accepted).toHaveBeenCalled();
    await waitFor(() => expect(container.innerHTML).toContain("No saved drafts"));
  });

  it("embedded workspace uses a full-page composer and saved batch rail", async () => {
    stubFetch(() => json([]));
    const { container } = render(<DraftWorkspace embedded capabilities={capabilities()} />);
    await waitFor(() => expect(container.innerHTML).toContain("No saved drafts"));
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(container.innerHTML).not.toContain(">Close<");
    expect(container.innerHTML).toContain("Create AI batch");
    expect(container.querySelector("[data-layout=extended-ai]")).not.toBeNull();
    expect(container.querySelector("aside")).not.toBeNull();
  });

  it("retries one browser transport failure without changing the request id", async () => {
    const batch = batchOf(draftRow());
    const ids: string[] = [];
    stubFetch(async (_url, init) => {
      if (init.method !== "POST") return json([]);
      ids.push((await generationParts(init)).id);
      if (ids.length === 1) throw new TypeError("Failed to fetch");
      return json(batch);
    });
    const { container } = render(<DraftWorkspace embedded capabilities={capabilities()} />);
    await waitFor(() => expect(container.innerHTML).toContain("Create AI batch"));
    fireEvent.change(container.querySelector("textarea")!, { target: { value: "Splatoon Raiders for 1 hour" } });
    fireEvent.click(buttonByText("Generate drafts"));
    await waitFor(() => expect(container.innerHTML).toContain("Review extracted sessions"));
    expect(ids).toHaveLength(2);
    expect(ids[0]).toBe(ids[1]);
  });

  it("a failed generation retries the original text and screenshot", async () => {
    const batch = batchOf(draftRow());
    const requests: Array<{ id: string; text: string; image: number[] | null; timezone: string }> = [];
    stubFetch(async (_url, init) => {
      if (init.method !== "POST") return json([]);
      requests.push({ ...(await generationParts(init)), timezone: String((init.body as FormData).get("timezone")) });
      return requests.length === 1 ? problem("Invalid extraction", 502) : json(batch);
    });
    const { container } = render(<DraftWorkspace embedded capabilities={capabilities()} />);
    await waitFor(() => expect(container.innerHTML).toContain("Create AI batch"));
    await addScreenshot(container);
    fireEvent.change(container.querySelector("textarea")!, { target: { value: "Hades yesterday for an hour" } });
    fireEvent.click(buttonByText("Generate drafts"));
    await waitFor(() => expect(container.innerHTML).toContain("Invalid extraction"));
    fireEvent.change(container.querySelector("textarea")!, { target: { value: "A different description" } });
    fireEvent.click(buttonByText("Retry generation"));
    await waitFor(() => expect(container.innerHTML).toContain("Review extracted sessions"));
    expect(requests).toHaveLength(2);
    expect(requests[0].id).toBe(requests[1].id);
    for (const request of requests) {
      expect(request.text).toBe("Hades yesterday for an hour");
      expect(request.image).toEqual([1, 2, 3]);
      expect(request.timezone).toBe("Europe/Warsaw");
    }
  });

  it("regeneration gets a fresh result and a failed regeneration keeps the previous batch", async () => {
    const first = batchOf(draftRow());
    const second = batchOf(draftRow({ title: "Hades II" }));
    const requests: Array<{ id: string; text: string; image: number[] | null }> = [];
    stubFetch(async (_url, init) => {
      if (init.method !== "POST") return json([]);
      requests.push(await generationParts(init));
      if (requests.length === 2) return problem("Invalid extraction", 502);
      return json(requests.length === 1 ? first : second);
    });
    const { container } = render(<DraftWorkspace embedded capabilities={capabilities()} />);
    await waitFor(() => expect(container.innerHTML).toContain("Create AI batch"));
    await addScreenshot(container);
    fireEvent.change(container.querySelector("textarea")!, { target: { value: "Hades yesterday for an hour" } });
    fireEvent.click(buttonByText("Generate drafts"));
    await waitFor(() => expect(container.innerHTML).toContain("Review extracted sessions"));
    fireEvent.click(buttonByText("Generate again"));
    await waitFor(() => expect(container.innerHTML).toContain("Invalid extraction"));
    expect(container.querySelector<HTMLInputElement>("input[data-game-search]")!.value).toBe("Hades");
    fireEvent.click(buttonByText("Retry generation"));
    await waitFor(() => expect(container.querySelector<HTMLInputElement>("input[data-game-search]")!.value).toBe("Hades II"));
    expect(requests).toHaveLength(3);
    expect(requests[0].id).not.toBe(requests[1].id);
    expect(requests[1].id).toBe(requests[2].id);
    for (const request of requests) {
      expect(request.text).toBe("Hades yesterday for an hour");
      expect(request.image).toEqual([1, 2, 3]);
    }
  });

  it("a resumed batch asks for new evidence instead of reusing another batch's input", async () => {
    const batch = batchOf(draftRow());
    stubFetch(batchServer(batch));
    const { container } = render(<DraftWorkspace embedded capabilities={capabilities()} />);
    await resume();
    await waitFor(() => expect(container.innerHTML).toContain("Review extracted sessions"));
    fireEvent.click(buttonByText("Generate again"));
    await waitFor(() => expect(container.innerHTML).toContain("Add the original description or screenshot"));
    expect(container.querySelector("textarea")).not.toBeNull();
  });

  it("review requires a local or IGDB match chosen from search results", async () => {
    const draft = draftRow({ selection: null, ready: false });
    const batch = batchOf(draft);
    const local: ManualMappingSearchResult = {
      source: "local",
      gameId: "saved-game",
      igdbId: null,
      title: "Hades",
      platform: "PC",
      platformId: 6,
      capsuleUrl: "https://covers.test/local.png",
    };
    const igdb: ManualMappingSearchResult = { ...local, source: "igdb", gameId: null, igdbId: "114283", capsuleUrl: "https://covers.test/igdb.png" };
    let searchQuery = "";
    let patchBody: unknown = null;
    stubFetch(
      batchServer(batch, (url, init) => {
        if (url.pathname.endsWith("/manual/search")) {
          searchQuery = url.search;
          return json({ query: "Hades", igdbAvailable: true, results: [local, igdb] });
        }
        if (init.method === "PATCH") {
          patchBody = JSON.parse(String(init.body));
          return json({ ...draft, revision: 2, selection: igdb, ready: true });
        }
        return json(null);
      }),
    );
    const { container } = render(<DraftWorkspace />);
    await resume();
    await waitFor(() => expect(container.querySelectorAll("[data-draft-row]")).toHaveLength(1));
    expect(container.querySelector("[data-draft-row-fields]")).not.toBeNull();
    expect(buttonByText("Accept")).toBeDisabled();

    fireEvent.click(buttonByText("Find game"));
    await waitFor(() => expect(container.querySelectorAll("[data-game-result]")).toHaveLength(2));
    expect(searchQuery).toBe("?query=Hades");
    expect(container.innerHTML).toContain("Saved game");
    expect(container.querySelectorAll("[data-game-result] img")).toHaveLength(2);

    await act(async () => {
      fireEvent.click(container.querySelectorAll("[data-game-result]")[1]);
    });
    await waitFor(() => expect(container.innerHTML).toContain("Matched from IGDB"));
    expect(patchBody).toMatchObject({ revision: 1, gameId: null, igdbId: "114283" });
    expect(buttonByText("Accept")).not.toBeDisabled();
    expect(container.querySelectorAll("[data-game-result]")).toHaveLength(0);
  });
});
