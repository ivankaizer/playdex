import { render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router";
import { ThemeProvider } from "@/lib/theme";
import { dashUrl, LandingPage } from "./LandingPage";

describe("LandingPage", () => {
  afterEach(() => vi.restoreAllMocks());

  it("links sign-in to the app host and never calls the API", () => {
    const fetchMock = vi.spyOn(globalThis, "fetch");
    render(
      <MemoryRouter>
        <ThemeProvider>
          <LandingPage />
        </ThemeProvider>
      </MemoryRouter>,
    );

    expect(screen.getByRole("link", { name: "Sign in" })).toHaveAttribute("href", dashUrl);
    expect(screen.getByRole("link", { name: "Open Playdex →" })).toHaveAttribute("href", dashUrl);
    expect(screen.queryByRole("form", { name: "Sign in" })).not.toBeInTheDocument();
    expect(screen.getByText("Top 5 games from recap")).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
