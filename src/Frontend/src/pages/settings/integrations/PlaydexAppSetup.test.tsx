import { act, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { apiBaseUrl } from "@/lib/api";
import { getConfig, setConfig } from "@/lib/config";
import { setupUrl } from "@/lib/downloads";
import { formatProviderName } from "@/lib/format";
import { PlaydexAppSetup } from "./PlaydexAppSetup";

const original = getConfig();

afterEach(() => {
  setConfig(original);
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe("PlaydexAppSetup", () => {
  it("shows the installer, the server this dashboard uses and the hidden key, each with Copy", async () => {
    setConfig({ apiBase: "https://dash.playdex.test/api/", sentry: null });
    const writeText = vi.fn().mockResolvedValue(undefined);
    vi.stubGlobal("navigator", { ...navigator, clipboard: { writeText } });
    vi.useFakeTimers();

    render(<PlaydexAppSetup apiKey="plx_secret" />);

    expect(screen.getByRole("link", { name: "Playdex-Setup.exe" })).toHaveAttribute("href", setupUrl);
    expect(screen.getByLabelText("Playdex server")).toHaveValue("https://dash.playdex.test/api");
    const key = screen.getByLabelText("API key");
    expect(key).toHaveValue("plx_secret");
    expect(key).toHaveAttribute("type", "password");

    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "Copy api key" }));
    });
    expect(writeText).toHaveBeenCalledWith("plx_secret");
    expect(screen.getByRole("button", { name: "Copy api key" })).toHaveTextContent("Copied");

    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "Copy playdex server" }));
    });
    expect(writeText).toHaveBeenLastCalledWith("https://dash.playdex.test/api");
    expect(screen.getByRole("button", { name: "Copy api key" })).toHaveTextContent("Copy");

    act(() => vi.advanceTimersByTime(2000));
    expect(screen.getByRole("button", { name: "Copy playdex server" })).toHaveTextContent("Copy");
  });

  it("keeps Copy when the browser refuses the clipboard", async () => {
    vi.stubGlobal("navigator", { ...navigator, clipboard: { writeText: vi.fn().mockRejectedValue(new Error("denied")) } });

    render(<PlaydexAppSetup apiKey="plx_secret" />);
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: "Copy api key" }));
    });

    expect(screen.getByRole("button", { name: "Copy api key" })).toHaveTextContent("Copy");
  });
});

describe("Playdex app naming and server", () => {
  it("names the api integration the Playdex app", () => {
    expect(formatProviderName("api")).toBe("Playdex app");
  });

  it("gives the server as an absolute URL without a trailing slash", () => {
    setConfig({ apiBase: "/api/", sentry: null });
    expect(apiBaseUrl()).toBe(`${window.location.origin}/api`);
    setConfig({ apiBase: "https://dash.playdex.test/api", sentry: null });
    expect(apiBaseUrl()).toBe("https://dash.playdex.test/api");
  });
});
