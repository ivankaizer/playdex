import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes } from "react-router";
import { ThemeProvider } from "@/lib/theme";
import { LandingPage } from "@/pages/landing/LandingPage";
import { LauncherPage, setupUrl } from "./LauncherPage";

function renderLanding(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <ThemeProvider>
        <Routes>
          <Route path="/" element={<LandingPage />} />
          <Route path="/launcher" element={<LauncherPage />} />
        </Routes>
      </ThemeProvider>
    </MemoryRouter>,
  );
}

describe("LauncherPage", () => {
  afterEach(() => vi.restoreAllMocks());

  it("opens from the launcher section on the landing", async () => {
    vi.spyOn(window, "scrollTo").mockImplementation(() => {});
    renderLanding("/");

    await userEvent.click(screen.getByRole("link", { name: /A console home screen for your Windows PC/ }));

    expect(screen.getByRole("heading", { level: 1, name: /Your gaming PC, as a console/ })).toBeInTheDocument();
  });

  it("links downloads to the public setup and never calls the API", () => {
    vi.spyOn(window, "scrollTo").mockImplementation(() => {});
    const fetchMock = vi.spyOn(globalThis, "fetch");
    renderLanding("/launcher");

    expect(screen.getByRole("link", { name: "Download for Windows" })).toHaveAttribute("href", setupUrl);
    expect(screen.getByRole("link", { name: "Playdex home" })).toHaveAttribute("href", "/");
    expect(screen.getByText("Install in three steps")).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
