import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router";
import { ThemeProvider } from "@/lib/theme";
import type { ProviderStatus } from "./models";

const apiRequest = vi.hoisted(() => vi.fn());
vi.mock("@/lib/api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api")>()),
  apiRequest,
}));

const { SettingsPage } = await import("./SettingsPage");

interface Deferred<T> {
  promise: Promise<T>;
  resolve: (value: T) => void;
  reject: (error: unknown) => void;
}

function deferred<T>(): Deferred<T> {
  let resolve!: (value: T) => void;
  let reject!: (error: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

function memoryStorage(): Storage {
  const items = new Map<string, string>();
  return {
    get length() {
      return items.size;
    },
    clear: () => items.clear(),
    getItem: (key) => items.get(key) ?? null,
    key: (index) => [...items.keys()][index] ?? null,
    removeItem: (key) => void items.delete(key),
    setItem: (key, value) => void items.set(key, String(value)),
  };
}

const steam: ProviderStatus = {
  id: 1,
  platform: "steam",
  accountId: "steam-account",
  status: "active",
  apiKey: null,
  apiKeyLocked: false,
};

function renderPage(entry = "/settings") {
  return render(
    <MemoryRouter initialEntries={[entry]}>
      <ThemeProvider>
        <SettingsPage />
      </ThemeProvider>
    </MemoryRouter>,
  );
}

type Handler = (path: string, options: { method?: string; body?: unknown; signal?: AbortSignal }) => Promise<unknown>;

function routeApi(overrides: Record<string, Handler> = {}) {
  apiRequest.mockImplementation((path: string, options: { method?: string } = {}) => {
    const key = `${options.method ?? "GET"} ${path.split("?")[0]}`;
    if (overrides[key]) return overrides[key](path, options);
    switch (key) {
      case "GET /providers":
        return Promise.resolve({ providers: [steam] });
      case "GET /settings":
        return Promise.resolve({ ignoreShortSessionsMinutes: 5, telegramChatId: null });
      case "GET /games/connections":
        return Promise.resolve({ connections: [] });
      default:
        return Promise.reject(new Error(`Unexpected ${key}`));
    }
  });
}

describe("SettingsPage", () => {
  beforeEach(() => {
    vi.stubGlobal("localStorage", memoryStorage());
    Element.prototype.scrollIntoView = vi.fn();
  });
  afterEach(() => {
    apiRequest.mockReset();
    vi.unstubAllGlobals();
  });

  it("shows loading states on first paint and loads providers, settings and connections", async () => {
    routeApi();
    renderPage();

    expect(screen.getByRole("button", { name: "Loading..." })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Refreshing..." })).toBeDisabled();
    expect(screen.queryByText("No integrations connected yet. Add one to start syncing playtime.")).toBeNull();

    expect(await screen.findByText("steam-account")).toBeInTheDocument();
    // Save stays disabled until a field changes.
    expect(await screen.findByRole("button", { name: "Save changes" })).toBeDisabled();
    expect(screen.getByRole("spinbutton")).toHaveValue("5");
    fireEvent.click(screen.getByRole("button", { name: "Increase minimum minutes" }));
    expect(screen.getByRole("spinbutton")).toHaveValue("6");
    expect(screen.getByRole("button", { name: "Save changes" })).toBeEnabled();
    fireEvent.click(screen.getByRole("button", { name: "Discard" }));
    expect(screen.getByRole("spinbutton")).toHaveValue("5");
    expect(screen.getByRole("button", { name: "Save changes" })).toBeDisabled();
    expect(await screen.findByText("No games connected yet. Pick two above to merge their playtime.")).toBeInTheDocument();

    const paths = apiRequest.mock.calls.map(([path]) => path as string);
    expect(paths.indexOf("/providers")).toBeLessThan(paths.indexOf("/settings"));
    expect(paths).toContain("/games/connections");
  });

  it("scrolls the #game-connections section into view", async () => {
    routeApi();
    renderPage("/settings#game-connections");
    await screen.findByText("No games connected yet. Pick two above to merge their playtime.");
    const section = document.getElementById("game-connections")!;
    expect(section.scrollIntoView).toHaveBeenCalled();
    expect(vi.mocked(Element.prototype.scrollIntoView).mock.contexts).toContain(section);
  });

  it("saves edited credentials with only the changed fields", async () => {
    const save = deferred<{ provider: ProviderStatus }>();
    const put = vi.fn<Handler>(() => save.promise);
    routeApi({ "PUT /providers/1": put });
    renderPage();

    fireEvent.click(await screen.findByRole("button", { name: "Manage" }));
    fireEvent.click(screen.getByRole("button", { name: "Save credentials" }));
    expect(screen.getByText("No changes to save.")).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Account ID"), { target: { value: "  renamed  " } });
    fireEvent.click(screen.getByRole("button", { name: "Save credentials" }));
    expect(put).toHaveBeenCalledWith("/providers/1", expect.objectContaining({ method: "PUT", body: { accountId: "renamed" } }));
    expect(screen.getByRole("button", { name: "Saving..." })).toBeDisabled();

    await act(async () => save.resolve({ provider: { ...steam, accountId: "renamed" } }));
    expect(screen.getByText("renamed")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Manage" })).toBeInTheDocument();
  });

  it("ignores a superseded game search after the query changes", async () => {
    const search = deferred<unknown>();
    routeApi({ "GET /games/search": () => search.promise });
    renderPage();
    await screen.findByText("No games connected yet. Pick two above to merge their playtime.");

    const section = within(document.getElementById("game-connections")!);
    const [primaryInput] = section.getAllByPlaceholderText("Search your library");
    fireEvent.change(primaryInput, { target: { value: "Hades" } });
    fireEvent.click(section.getAllByRole("button", { name: "Search" })[0]);
    expect(apiRequest).toHaveBeenCalledWith("/games/search?query=Hades", expect.anything());
    expect(section.getByRole("button", { name: "Searching..." })).toBeDisabled();

    fireEvent.change(primaryInput, { target: { value: "Hades II" } });
    expect(section.getAllByRole("button", { name: "Search" })).toHaveLength(2);

    await act(async () =>
      search.resolve({ query: "Hades", results: [{ gameId: "1", title: "Hades", platform: "steam", capsuleUrl: null }] }),
    );
    await waitFor(() => expect(screen.getByText("Search to find the primary game.")).toBeInTheDocument());
  });

  it("shows 0 minutes as Off, clamps a stored value above 60 and lets saving write it back", async () => {
    const put = vi.fn<Handler>(() => Promise.resolve({ ignoreShortSessionsMinutes: 60, telegramChatId: null }));
    routeApi({
      "GET /settings": () => Promise.resolve({ ignoreShortSessionsMinutes: 120, telegramChatId: null }),
      "PUT /settings": put,
    });
    renderPage();

    const spin = await screen.findByRole("spinbutton");
    await waitFor(() => expect(spin).toHaveValue("60"));
    expect(screen.getByRole("button", { name: "Increase minimum minutes" })).toBeDisabled();
    expect(screen.getByText(/Saved minimum is above 60 min/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Discard" })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Save changes" }));
    expect(put).toHaveBeenCalledWith(
      "/settings",
      expect.objectContaining({ body: { ignoreShortSessionsMinutes: 60, telegramChatId: null } }),
    );
    await waitFor(() => expect(screen.getByRole("button", { name: "Save changes" })).toBeDisabled());

    fireEvent.click(screen.getByRole("button", { name: "Off" }));
    expect(spin).toHaveValue("Off");
    expect(spin).toHaveAttribute("aria-valuetext", "Off");
    expect(screen.getByRole("button", { name: "Discard" })).toBeInTheDocument();
  });

  it("blocks saving an invalid chat ID with an inline note", async () => {
    routeApi();
    renderPage();
    const input = await screen.findByPlaceholderText("Chat ID");
    await waitFor(() => expect(input).toBeEnabled());
    fireEvent.change(input, { target: { value: "@channel" } });
    expect(input).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByText("Use a numeric chat ID like 77434259 or -1001234567890.")).toBeInTheDocument();
    expect(screen.getByText("Fix the chat ID to save")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Save changes" })).toBeDisabled();
  });

  it("lists the first 10 game connections with a pluralized count and Show all", async () => {
    const connections = Array.from({ length: 12 }, (_, index) => ({
      gameId: `p${index}`,
      title: `Primary game ${index}`,
      platform: "steam",
      capsuleUrl: null,
      connectedGameId: `s${index}`,
      connectedTitle: `Merged game ${index}`,
      connectedPlatform: "psn",
      connectedCapsuleUrl: null,
    }));
    routeApi({ "GET /games/connections": () => Promise.resolve({ connections }) });
    renderPage();

    expect(await screen.findByText("12 pairs")).toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: /^Unlink / })).toHaveLength(10);
    expect(screen.getByRole("link", { name: "Primary game 0" })).toHaveAttribute("title", "Primary game 0");
    fireEvent.click(screen.getByRole("button", { name: "Show all 12" }));
    expect(screen.getAllByRole("button", { name: /^Unlink / })).toHaveLength(12);
    fireEvent.click(screen.getByRole("button", { name: "Show fewer" }));
    expect(screen.getAllByRole("button", { name: /^Unlink / })).toHaveLength(10);
  });

  it("keeps long integration IDs whole in the tooltip and shows the first 6 integrations", async () => {
    const longId = "thisisaverylongusernamethatisall (76561198012345678)";
    const providers = Array.from({ length: 7 }, (_, index) => ({ ...steam, id: index + 1, accountId: index === 0 ? longId : `acct-${index}` }));
    routeApi({ "GET /providers": () => Promise.resolve({ providers }) });
    renderPage();

    expect(await screen.findByTitle(longId)).toHaveTextContent("thisisaverylongusern…198012345678)");
    expect(screen.getByText("7 of 7 active")).toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: "Manage" })).toHaveLength(6);
    fireEvent.click(screen.getByRole("button", { name: "Show all 7" }));
    expect(screen.getAllByRole("button", { name: "Manage" })).toHaveLength(7);
  });
  it("creates the Playdex app key when there is none and then shows its setup values", async () => {
    const app: ProviderStatus = { id: 2, platform: "api", accountId: null, status: "active", apiKey: "plx_new", apiKeyLocked: false };
    let providers: ProviderStatus[] = [steam];
    const post = vi.fn(() => {
      providers = [steam, app];
      return Promise.resolve({ apiKey: "plx_new" });
    });
    routeApi({ "GET /providers": () => Promise.resolve({ providers }), "POST /api/key": post });
    renderPage();

    fireEvent.click(await screen.findByRole("button", { name: "Create API key" }));

    expect(await screen.findByLabelText("API key")).toHaveValue("plx_new");
    expect(post).toHaveBeenCalledWith("/api/key", expect.objectContaining({ method: "POST" }));
    expect(screen.queryByRole("button", { name: "Create API key" })).toBeNull();
  });
});
