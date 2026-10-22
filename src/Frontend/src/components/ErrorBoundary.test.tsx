import { render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ErrorBoundary } from "./ErrorBoundary";

function Broken({ broken }: { broken: boolean }) {
  if (broken) throw new TypeError("Cannot read properties of undefined (reading 'map')");
  return <p>Page content</p>;
}

describe("ErrorBoundary", () => {
  afterEach(() => vi.restoreAllMocks());

  it("shows a way out instead of a blank page, and recovers when the page changes", () => {
    vi.spyOn(console, "error").mockImplementation(() => undefined);
    const { rerender } = render(
      <ErrorBoundary resetKey="/sessions">
        <Broken broken />
      </ErrorBoundary>,
    );

    expect(screen.getByRole("alert")).toHaveTextContent("Something went wrong");
    expect(screen.queryByText(/Cannot read properties/)).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reload page" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Go to dashboard" })).toHaveAttribute("href", "/dashboard");

    rerender(
      <ErrorBoundary resetKey="/dashboard">
        <Broken broken={false} />
      </ErrorBoundary>,
    );

    expect(screen.getByText("Page content")).toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});
