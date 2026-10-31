import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api";

const apiRequest = vi.hoisted(() => vi.fn());
vi.mock("@/lib/api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api")>()),
  apiRequest,
}));

const { ConnectedAppsSection } = await import("./ConnectedAppsSection");

const claude = {
  id: "0b6f6a54-0000-4000-8000-000000000001",
  clientId: "https://claude.ai/oauth/claude-code-client-metadata",
  clientName: "Claude Code",
  scope: "drafts",
  connectedAt: "2026-10-30T10:00:00Z",
  lastUsedAt: "2026-10-31T10:00:00Z",
};

describe("ConnectedAppsSection", () => {
  beforeEach(() => apiRequest.mockReset());

  it("lists connected apps and disconnects one", async () => {
    apiRequest.mockResolvedValueOnce([claude]).mockResolvedValueOnce(undefined);
    render(<ConnectedAppsSection />);

    expect(await screen.findByText("Claude Code")).toBeInTheDocument();
    expect(screen.getByText(/claude\.ai · connected/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Disconnect Claude Code" }));

    await waitFor(() => expect(screen.queryByText("Claude Code")).not.toBeInTheDocument());
    expect(apiRequest).toHaveBeenLastCalledWith(`/oauth/connections/${claude.id}`, { method: "DELETE" });
    expect(screen.getByText("No apps are connected.")).toBeInTheDocument();
  });

  it("keeps the app and shows why when disconnecting fails", async () => {
    apiRequest.mockResolvedValueOnce([claude]).mockRejectedValueOnce(new ApiError(404, "Connection not found."));
    render(<ConnectedAppsSection />);

    fireEvent.click(await screen.findByRole("button", { name: "Disconnect Claude Code" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Connection not found.");
    expect(screen.getByText("Claude Code")).toBeInTheDocument();
  });
});
