import { fireEvent, render, screen } from "@testing-library/react";
import { checkDuration } from "../sessionsState";
import { SessionEditModal } from "./SessionEditModal";

describe("SessionEditModal", () => {
  it("renders an accessible start and duration editor", () => {
    const { container } = render(<SessionEditModal gameTitle="Hades II" startedAt="2026-08-04T18:30:15" duration="2h 15m" />);
    const dialog = screen.getByRole("dialog");
    expect(dialog).toHaveAttribute("aria-modal", "true");
    expect(dialog).toHaveTextContent("Hades II");
    const started = container.querySelector<HTMLInputElement>("input[type=datetime-local]")!;
    expect(started).toHaveAttribute("step", "1");
    expect(started.getAttribute("value")).toBe("2026-08-04T18:30:15");
    expect(started).toHaveFocus();
    expect(screen.getByLabelText(/Duration/)).toHaveValue("2h 15m");
    expect(container.querySelector("button[data-action=cancel]")?.textContent).toBe("Cancel");
    expect(container.querySelector("button[data-action=save]")?.textContent).toBe("Save session");
    expect(container.querySelector("button[data-action=save]")).toBeEnabled();
  });

  it("keeps Tab inside the dialog", () => {
    render(
      <>
        <button type="button">Behind the dialog</button>
        <SessionEditModal gameTitle="Hades II" startedAt="2026-08-04T18:30:15" duration="2h 15m" />
      </>,
    );
    const dialog = screen.getByRole("dialog");
    const close = screen.getByRole("button", { name: "Close session editor" });
    const save = screen.getByRole("button", { name: "Save session" });

    save.focus();
    fireEvent.keyDown(save, { key: "Tab" });
    expect(close).toHaveFocus();

    fireEvent.keyDown(close, { key: "Tab", shiftKey: true });
    expect(save).toHaveFocus();
    expect(dialog).toContainElement(document.activeElement as HTMLElement);
  });

  it("blocks saving a zero or malformed duration and explains why", () => {
    const onSave = vi.fn();
    const { container, rerender } = render(
      <SessionEditModal
        gameTitle="Rez"
        startedAt="2026-08-04T18:30:15"
        duration="0m"
        durationCheck={checkDuration("0m")}
        onSave={onSave}
      />,
    );
    const save = container.querySelector<HTMLButtonElement>("button[data-action=save]")!;
    const input = screen.getByLabelText(/Duration/);
    expect(save).toBeDisabled();
    expect(input).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByText("A session needs at least 1 minute.")).toBeInTheDocument();
    fireEvent.submit(save.closest("form")!);
    expect(onSave).not.toHaveBeenCalled();

    rerender(
      <SessionEditModal
        gameTitle="Rez"
        startedAt="2026-08-04T18:30:15"
        duration="75m"
        durationCheck={checkDuration("75m")}
        onSave={onSave}
      />,
    );
    expect(screen.getByText("Minutes go up to 59. Write 1h 15m instead.")).toBeInTheDocument();
    expect(save).toBeDisabled();
  });

  it("refuses more than 24 hours", () => {
    const onSave = vi.fn();
    const { container } = render(
      <SessionEditModal
        gameTitle="Rez"
        startedAt="2026-08-04T18:30:15"
        duration="26h 5m"
        durationCheck={checkDuration("26h 5m")}
        onSave={onSave}
      />,
    );
    const save = container.querySelector<HTMLButtonElement>("button[data-action=save]")!;
    expect(screen.getByText("A session can be at most 24 hours.")).toHaveAttribute("data-duration-hint", "error");
    expect(save).toHaveTextContent("Save session");
    expect(save).toBeDisabled();
    expect(screen.getByLabelText(/Duration/)).toHaveAttribute("aria-invalid", "true");
    fireEvent.click(save);
    expect(onSave).not.toHaveBeenCalled();
  });

  it("reports typed durations and closes on Escape", () => {
    const onDurationChanged = vi.fn();
    const onCancel = vi.fn();
    render(
      <SessionEditModal
        gameTitle="Rez"
        startedAt="2026-08-04T18:30:15"
        duration="1h"
        onDurationChanged={onDurationChanged}
        onCancel={onCancel}
      />,
    );
    fireEvent.change(screen.getByLabelText(/Duration/), { target: { value: "1h 30m" } });
    expect(onDurationChanged).toHaveBeenCalledWith("1h 30m");
    fireEvent.keyDown(screen.getByRole("dialog"), { key: "Escape" });
    expect(onCancel).toHaveBeenCalledTimes(1);
  });
});
