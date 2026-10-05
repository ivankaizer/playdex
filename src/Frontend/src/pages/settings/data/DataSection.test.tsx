import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api";

const apiRequest = vi.hoisted(() => vi.fn());
const apiBlob = vi.hoisted(() => vi.fn());
const logoutSession = vi.hoisted(() => vi.fn());
vi.mock("@/lib/api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api")>()),
  apiRequest,
  apiBlob,
}));
vi.mock("@/lib/auth", async (importOriginal) => {
  const original = await importOriginal<typeof import("@/lib/auth")>();
  return { ...original, authSession: { ...original.authSession, logout: logoutSession } };
});

const { DataSection } = await import("./DataSection");

describe("DataSection", () => {
  const assign = vi.fn();

  beforeEach(() => {
    apiRequest.mockReset();
    apiBlob.mockReset();
    logoutSession.mockReset();
    assign.mockReset();
    vi.stubGlobal("location", { ...window.location, assign });
    URL.createObjectURL = vi.fn(() => "blob:export");
    URL.revokeObjectURL = vi.fn();
  });

  afterEach(() => vi.unstubAllGlobals());

  it("downloads the export as a JSON file", async () => {
    apiBlob.mockResolvedValue(new Blob(["{}"], { type: "application/json" }));
    const click = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => {});
    render(<DataSection />);

    fireEvent.click(screen.getByRole("button", { name: "Download my data" }));

    await screen.findByText("✓ Download started");
    expect(apiBlob).toHaveBeenCalledWith("/me/export", expect.anything());
    expect(click).toHaveBeenCalledOnce();
    expect(URL.revokeObjectURL).toHaveBeenCalledWith("blob:export");
    click.mockRestore();
  });

  it("requires the password and shows a wrong-password error without signing out", async () => {
    apiRequest.mockRejectedValue(new ApiError(403, "Password is incorrect."));
    render(<DataSection />);

    fireEvent.click(screen.getByRole("button", { name: "Delete account…" }));
    const submit = screen.getByRole("button", { name: "Permanently delete account" });
    expect(submit).toBeDisabled();

    fireEvent.change(screen.getByLabelText("Enter your password to confirm"), { target: { value: "nope" } });
    fireEvent.click(submit);

    expect(await screen.findByRole("alert")).toHaveTextContent("Password is incorrect.");
    expect(apiRequest).toHaveBeenCalledWith("/me", expect.objectContaining({ method: "DELETE", body: { password: "nope" } }));
    expect(logoutSession).not.toHaveBeenCalled();
    expect(assign).not.toHaveBeenCalled();
  });

  it("signs out and returns home after deleting", async () => {
    apiRequest.mockResolvedValue(undefined);
    render(<DataSection />);

    fireEvent.click(screen.getByRole("button", { name: "Delete account…" }));
    fireEvent.change(screen.getByLabelText("Enter your password to confirm"), { target: { value: "secret" } });
    fireEvent.click(screen.getByRole("button", { name: "Permanently delete account" }));

    await waitFor(() => expect(assign).toHaveBeenCalledWith("/"));
    expect(logoutSession).toHaveBeenCalledOnce();
  });

  it("cancel closes the confirmation and clears the password", () => {
    render(<DataSection />);
    fireEvent.click(screen.getByRole("button", { name: "Delete account…" }));
    fireEvent.change(screen.getByLabelText("Enter your password to confirm"), { target: { value: "secret" } });
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    fireEvent.click(screen.getByRole("button", { name: "Delete account…" }));
    expect(screen.getByLabelText("Enter your password to confirm")).toHaveValue("");
  });
});
