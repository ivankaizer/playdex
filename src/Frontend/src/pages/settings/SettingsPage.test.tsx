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
    expect(screen.getByRole("spinbutton")).toHaveValue(5);
    fireEvent.click(screen.getByRole("button", { name: "Increase minimum minutes" }));
    expect(screen.getByRole("spinbutton")).toHaveValue(6);
    expect(screen.getByRole("button", { name: "Save changes" })).toBeEnabled();
    fireEvent.click(screen.getByRole("button", { name: "Discard" }));
    expect(screen.getByRole("spinbutton")).toHaveValue(5);
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
});
