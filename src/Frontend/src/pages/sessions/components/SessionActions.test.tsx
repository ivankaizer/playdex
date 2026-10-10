import { fireEvent, render, screen } from "@testing-library/react";
import { SessionActions } from "./SessionActions";

describe("SessionActions", () => {
  it("offers edit and remove for finished sessions", () => {
    const { container } = render(<SessionActions canEdit />);
    expect(container.querySelector("button[data-action=edit]")?.textContent).toBe("Edit");
    expect(container.querySelector("button[data-action=remove]")?.textContent).toBe("Remove");
  });

  it("does not offer edit for active sessions", () => {
    const { container } = render(<SessionActions canEdit={false} />);
    expect(container.querySelectorAll("button[data-action=edit]")).toHaveLength(0);
    expect(container.querySelectorAll("button[data-action=remove]")).toHaveLength(1);
    fireEvent.click(screen.getByRole("button", { name: "Session actions" }));
    expect(screen.getAllByRole("menuitem").map((item) => item.textContent)).toEqual(["Remove"]);
  });

  describe("compact menu", () => {
    const setup = () => {
      const onEdit = vi.fn();
      const onRemove = vi.fn();
      render(<SessionActions sessionTitle="Hades II" canEdit onEdit={onEdit} onRemove={onRemove} />);
      const trigger = screen.getByRole("button", { name: "Actions for Hades II" });
      return { trigger, onEdit, onRemove };
    };

    it("is a menu button that opens with focus on the first item", () => {
      const { trigger } = setup();
      expect(trigger).toHaveAttribute("aria-haspopup", "menu");
      expect(trigger).toHaveAttribute("aria-expanded", "false");
      expect(screen.queryByRole("menu")).toBeNull();

      fireEvent.click(trigger);
      expect(trigger).toHaveAttribute("aria-expanded", "true");
      const menu = screen.getByRole("menu", { name: "Actions for Hades II" });
      expect(trigger).toHaveAttribute("aria-controls", menu.id);
      expect(screen.getByRole("menuitem", { name: "Edit" })).toHaveFocus();
    });

    it("moves with arrow keys, Home and End, wrapping at the ends", () => {
      const { trigger } = setup();
      fireEvent.keyDown(trigger, { key: "ArrowDown" });
      const menu = screen.getByRole("menu");
      const edit = screen.getByRole("menuitem", { name: "Edit" });
      const remove = screen.getByRole("menuitem", { name: "Remove" });
      expect(edit).toHaveFocus();
      fireEvent.keyDown(menu, { key: "ArrowDown" });
      expect(remove).toHaveFocus();
      fireEvent.keyDown(menu, { key: "ArrowDown" });
      expect(edit).toHaveFocus();
      fireEvent.keyDown(menu, { key: "ArrowUp" });
      expect(remove).toHaveFocus();
      fireEvent.keyDown(menu, { key: "Home" });
      expect(edit).toHaveFocus();
      fireEvent.keyDown(menu, { key: "End" });
      expect(remove).toHaveFocus();
      expect(edit).toHaveAttribute("tabindex", "-1");
      expect(remove).toHaveAttribute("tabindex", "0");
    });

    it("ArrowUp on the button opens on the last item", () => {
      const { trigger } = setup();
      fireEvent.keyDown(trigger, { key: "ArrowUp" });
      expect(screen.getByRole("menuitem", { name: "Remove" })).toHaveFocus();
    });

    it("Escape closes and returns focus to the button", () => {
      const { trigger } = setup();
      fireEvent.click(trigger);
      fireEvent.keyDown(screen.getByRole("menu"), { key: "Escape" });
      expect(screen.queryByRole("menu")).toBeNull();
      expect(trigger).toHaveFocus();
      expect(trigger).toHaveAttribute("aria-expanded", "false");
    });

    it("Tab and a click outside close it", () => {
      const { trigger } = setup();
      fireEvent.click(trigger);
      fireEvent.keyDown(screen.getByRole("menu"), { key: "Tab" });
      expect(screen.queryByRole("menu")).toBeNull();

      fireEvent.click(trigger);
      fireEvent.pointerDown(document.body);
      expect(screen.queryByRole("menu")).toBeNull();
    });

    it("runs the chosen action and closes", () => {
      const { trigger, onEdit, onRemove } = setup();
      fireEvent.click(trigger);
      fireEvent.click(screen.getByRole("menuitem", { name: "Edit" }));
      expect(onEdit).toHaveBeenCalledTimes(1);
      expect(screen.queryByRole("menu")).toBeNull();

      fireEvent.click(trigger);
      fireEvent.click(screen.getByRole("menuitem", { name: "Remove" }));
      expect(onRemove).toHaveBeenCalledTimes(1);
    });

    it("is disabled while the row is being removed", () => {
      render(<SessionActions sessionTitle="Rez" canEdit disabled />);
      expect(screen.getByRole("button", { name: "Actions for Rez" })).toBeDisabled();
    });
  });
});
