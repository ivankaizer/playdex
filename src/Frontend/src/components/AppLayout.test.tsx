import { render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router";
import { authSession } from "@/lib/auth";
import { ThemeProvider } from "@/lib/theme";

const apiRequest = vi.hoisted(() => vi.fn());
vi.mock("@/lib/api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api")>()),
  apiRequest,
}));

const { AppLayout } = await import("./AppLayout");

function renderLayout() {
  return render(
    <MemoryRouter initialEntries={["/dashboard"]}>
      <ThemeProvider>
        <AppLayout title="Dashboard" />
      </ThemeProvider>
    </MemoryRouter>,
  );
}

const header = () => screen.getByRole("navigation", { name: "Main" });

describe("AppLayout admin entry", () => {
  beforeEach(() => {
    apiRequest.mockReset();
    authSession.logout();
  });

  afterEach(() => authSession.logout());

  it("hides the Admin link from regular users", async () => {
    apiRequest.mockResolvedValue({ userId: 1, email: "user@example.test", isAdmin: false });
    renderLayout();

    await waitFor(() => expect(apiRequest).toHaveBeenCalledWith("/me", expect.anything()));
    expect(screen.queryByRole("button", { name: "Admin" })).toBeNull();
    expect(authSession.isAdmin()).toBe(false);
  });

  it("shows the Admin link once /me confirms the role, in the header and the footer", async () => {
    apiRequest.mockResolvedValue({ userId: 1, email: "admin@example.test", isAdmin: true });
    renderLayout();

    expect(await screen.findByRole("button", { name: "Admin" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Admin" })).toHaveAttribute("href", "/admin");
    expect(authSession.isAdmin()).toBe(true);
  });

  it("renders the stored flag immediately and drops it when the role is revoked", async () => {
    authSession.storeProfile({ userId: 1, email: "admin@example.test", isAdmin: true });
    apiRequest.mockResolvedValue({ userId: 1, email: "admin@example.test", isAdmin: false });
    renderLayout();

    expect(screen.getByRole("button", { name: "Admin" })).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByRole("button", { name: "Admin" })).toBeNull());
    expect(header()).toBeInTheDocument();
  });

  it("keeps the stored flag when /me fails", async () => {
    authSession.storeProfile({ userId: 1, email: "admin@example.test", isAdmin: true });
    apiRequest.mockRejectedValue(new Error("offline"));
    renderLayout();

    await waitFor(() => expect(apiRequest).toHaveBeenCalled());
    expect(screen.getByRole("button", { name: "Admin" })).toBeInTheDocument();
  });
});
