import { createEvent, fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router";
import { AppLink } from "./AppLink";

function renderLink() {
  return render(
    <MemoryRouter initialEntries={["/dashboard"]}>
      <Routes>
        <Route path="/dashboard" element={<AppLink to="/sessions">View all</AppLink>} />
        <Route path="/sessions" element={<h1>Sessions</h1>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe("AppLink", () => {
  it("is a real link that a plain click follows inside the app", () => {
    renderLink();
    const link = screen.getByRole("link", { name: "View all" });
    expect(link).toHaveAttribute("href", "/sessions");

    fireEvent.click(link);

    expect(screen.getByRole("heading", { name: "Sessions" })).toBeInTheDocument();
  });

  it("leaves a Ctrl, ⌘ or middle click to the browser (a new tab)", () => {
    renderLink();
    const link = screen.getByRole("link", { name: "View all" });
    // Records whether the app let the click through, then stops jsdom from trying to load the page.
    const letThrough: boolean[] = [];
    const record = (event: MouseEvent) => {
      letThrough.push(!event.defaultPrevented);
      event.preventDefault();
    };
    document.addEventListener("click", record);

    for (const init of [{ ctrlKey: true }, { metaKey: true }, { button: 1 }]) {
      fireEvent(link, createEvent.click(link, init));
    }
    document.removeEventListener("click", record);

    expect(letThrough).toEqual([true, true, true]);
    expect(screen.queryByRole("heading", { name: "Sessions" })).not.toBeInTheDocument();
  });
});
