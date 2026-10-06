import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { BarChart, Donut, StackedBar } from "./ui";

const segments = [
  { key: "steam", label: "Steam", value: 75, color: "#e07a0f", detail: "30h" },
  { key: "psn", label: "PlayStation", value: 25, color: "#7c3aed" },
];

describe("chart tooltips", () => {
  it("shows a bar's text on hover and on keyboard focus, even for an empty bar", () => {
    render(
      <BarChart
        data={[
          { key: "mon", value: 3600, label: "Mon", title: "Mon · 1h" },
          { key: "tue", value: 0, label: "Tue", title: "Tue · no play" },
        ]}
      />,
    );
    expect(screen.queryByRole("tooltip")).toBeNull();

    fireEvent.mouseEnter(screen.getByLabelText("Tue · no play"));
    expect(screen.getByRole("tooltip")).toHaveTextContent("Tue · no play");

    fireEvent.focus(screen.getByLabelText("Mon · 1h"));
    expect(screen.getByRole("tooltip")).toHaveTextContent("Mon · 1h");
  });

  it("shows a stacked bar segment's share and detail on hover", () => {
    const { container } = render(<StackedBar segments={segments} />);
    const parts = container.querySelectorAll("[role=img] > div");
    fireEvent.mouseEnter(parts[0]);
    expect(screen.getByRole("tooltip")).toHaveTextContent("Steam · 75% · 30h");
    fireEvent.mouseEnter(parts[1]);
    expect(screen.getByRole("tooltip")).toHaveTextContent("PlayStation · 25%");
  });

  it("finds the donut segment under the pointer and ignores the hole", () => {
    render(<Donut segments={segments} size={100} thickness={20} />);
    const donut = screen.getByRole("img");
    donut.getBoundingClientRect = () => ({ left: 0, top: 0, width: 100, height: 100, right: 100, bottom: 100, x: 0, y: 0, toJSON: () => ({}) });

    // Right edge: 90° clockwise from 12 o'clock is 25%, inside Steam's 0–75%.
    fireEvent.mouseMove(donut, { clientX: 95, clientY: 50 });
    expect(screen.getByRole("tooltip")).toHaveTextContent("Steam · 75% · 30h");

    // Left edge: 270° is 75%, the start of PlayStation's range.
    fireEvent.mouseMove(donut, { clientX: 5, clientY: 49 });
    expect(screen.getByRole("tooltip")).toHaveTextContent("PlayStation · 25%");

    fireEvent.mouseMove(donut, { clientX: 50, clientY: 50 });
    expect(screen.queryByRole("tooltip")).toBeNull();
  });
});
