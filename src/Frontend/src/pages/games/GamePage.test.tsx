import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { ThemeProvider } from "@/lib/theme";
import { GamePage } from "./GamePage";

describe("GamePage", () => {
  it("renders the invalid link state without requests", () => {
    const fetchSpy = vi.fn();
    vi.stubGlobal("fetch", fetchSpy);
    render(
      <MemoryRouter initialEntries={["/games/bad"]}>
        <ThemeProvider>
          <GamePage hash={null} />
        </ThemeProvider>
      </MemoryRouter>,
    );

    expect(screen.getByRole("heading", { name: "Unknown game" })).toBeInTheDocument();
    expect(screen.getAllByText("Invalid game link.")).toHaveLength(4);
    expect(screen.getByText("Unknown")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Ignore game" })).toBeDisabled();
    expect(fetchSpy).not.toHaveBeenCalled();
    vi.unstubAllGlobals();
  });
});
