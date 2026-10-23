import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { MemoryRouter } from "react-router";
import { ThemeProvider } from "@/lib/theme";
import { PrivacyPage } from "./PrivacyPage";

function renderPage() {
  return render(
    <MemoryRouter initialEntries={["/privacy"]}>
      <ThemeProvider>
        <PrivacyPage />
      </ThemeProvider>
    </MemoryRouter>,
  );
}

describe("PrivacyPage", () => {
  it("renders every section with an in-page link to it", () => {
    renderPage();
    expect(screen.getByRole("heading", { level: 1, name: "Your data, plainly explained" })).toBeInTheDocument();

    const toc = screen.getByRole("navigation", { name: "On this page" });
    const links = within(toc).getAllByRole("link");
    expect(links.length).toBeGreaterThanOrEqual(6);
    for (const link of links) {
      const id = link.getAttribute("href")!.slice(1);
      expect(document.getElementById(id)).not.toBeNull();
    }
  });

  it("is reachable signed out and links the footer to the real repo", () => {
    renderPage();
    expect(screen.getByRole("link", { name: "Sign in" })).toHaveAttribute("href", "/");
    expect(screen.getByRole("link", { name: "GitHub ↗" })).toHaveAttribute("href", "https://github.com/ivankaizer/playdex");
    expect(screen.getByRole("link", { name: "Privacy" })).toHaveAttribute("href", "/privacy");
  });
});
