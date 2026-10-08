import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router";
import { ThemeProvider } from "@/lib/theme";
import { HomePage } from "./HomePage";

function renderHome() {
  return render(
    <MemoryRouter>
      <ThemeProvider>
        <HomePage />
      </ThemeProvider>
    </MemoryRouter>,
  );
}

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
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

describe("HomePage", () => {
  // Some Node versions expose an unusable global localStorage that shadows jsdom's.
  beforeEach(() => vi.stubGlobal("localStorage", memoryStorage()));
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it("shows the cached email for a logged in session without fetching the profile", () => {
    localStorage.setItem("playdex.token", "token");
    localStorage.setItem("playdex.email", "cached@example.com");
    const fetchMock = vi.spyOn(globalThis, "fetch");

    renderHome();

    // Shown in the header pill bar and in the auth card.
    expect(screen.getAllByText("cached@example.com")).toHaveLength(2);
    expect(screen.getByText("Session active.")).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("loads the profile when a token exists without a cached email", async () => {
    localStorage.setItem("playdex.token", "token");
    const fetchMock = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValue(jsonResponse(200, { data: { userId: 1, email: "loaded@example.com" }, error: null }));

    renderHome();
    expect(screen.getAllByText("Loading...").length).toBeGreaterThan(0);

    expect((await screen.findAllByText("loaded@example.com")).length).toBeGreaterThan(0);
    expect(String(fetchMock.mock.calls[0][0])).toMatch(/\/me$/);
    expect(localStorage.getItem("playdex.email")).toBe("loaded@example.com");
  });

  it("posts credentials and shows the login error", async () => {
    const fetchMock = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValue(jsonResponse(401, { data: null, error: { error: "Invalid credentials." } }));

    renderHome();
    const form = within(screen.getByRole("form", { name: "Sign in" }));
    fireEvent.change(screen.getByPlaceholderText("you@example.com"), { target: { value: "a@b.c" } });
    fireEvent.change(screen.getByPlaceholderText("••••••••"), { target: { value: "secret" } });
    fireEvent.click(form.getByRole("button", { name: "Sign in" }));

    expect(await screen.findByText("Invalid credentials.")).toBeInTheDocument();
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toMatch(/\/login$/);
    expect(init?.method).toBe("POST");
    expect(init?.body).toBe(JSON.stringify({ email: "a@b.c", password: "secret" }));
    await waitFor(() => expect(form.getByRole("button", { name: "Sign in" })).not.toBeDisabled());
  });

  it("focuses the email field from the header sign-in pill", () => {
    renderHome();
    const form = screen.getByRole("form", { name: "Sign in" });
    const headerButton = screen.getAllByRole("button", { name: "Sign in" }).find((button) => !form.contains(button));
    fireEvent.click(headerButton!);
    expect(screen.getByPlaceholderText("you@example.com")).toHaveFocus();
  });
});
