import { dialogKeyDown } from "./focusTrap";

function dialogWithButtons() {
  const dialog = document.createElement("section");
  const first = document.createElement("button");
  const last = document.createElement("button");
  dialog.append(first, last);
  document.body.append(dialog);
  return { dialog, first, last };
}

describe("dialogKeyDown", () => {
  afterEach(() => document.body.replaceChildren());

  it("closes on Escape and stops it there", () => {
    const { dialog } = dialogWithButtons();
    const close = vi.fn();
    const event = new KeyboardEvent("keydown", { key: "Escape", cancelable: true });

    dialogKeyDown(event, dialog, close);

    expect(close).toHaveBeenCalledTimes(1);
    expect(event.defaultPrevented).toBe(true);
  });

  it("does nothing on Escape while busy (no close)", () => {
    const { dialog } = dialogWithButtons();
    const event = new KeyboardEvent("keydown", { key: "Escape", cancelable: true });

    dialogKeyDown(event, dialog, undefined);

    expect(event.defaultPrevented).toBe(false);
  });

  it("leaves an Escape that a control inside already handled", () => {
    const { dialog } = dialogWithButtons();
    const close = vi.fn();
    const event = new KeyboardEvent("keydown", { key: "Escape", cancelable: true });
    event.preventDefault();

    dialogKeyDown(event, dialog, close);

    expect(close).not.toHaveBeenCalled();
  });

  it("still keeps Tab inside", () => {
    const { dialog, first, last } = dialogWithButtons();
    last.focus();

    dialogKeyDown(new KeyboardEvent("keydown", { key: "Tab", cancelable: true }), dialog, vi.fn());

    expect(first).toHaveFocus();
  });
});
