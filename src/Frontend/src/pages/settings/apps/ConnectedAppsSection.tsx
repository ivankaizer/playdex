import { useEffect, useState } from "react";
import { apiRequest, errorMessage, isAbortError } from "@/lib/api";
import { formatDateLabel } from "@/lib/format";
import { SectionHeading } from "../shared/SectionHeading";

export interface OAuthConnection {
  id: string;
  clientId: string;
  clientName: string;
  scope: string;
  connectedAt: string;
  lastUsedAt: string | null;
}

type Load = { status: "loading" } | { status: "error"; message: string } | { status: "ready"; connections: OAuthConnection[] };

function hostOf(clientId: string): string {
  try {
    return new URL(clientId).host;
  } catch {
    return clientId;
  }
}

/** Settings card: apps signed in to Playdex with OAuth, such as assistants connected over MCP, and disconnecting them. */
export function ConnectedAppsSection() {
  const [load, setLoad] = useState<Load>({ status: "loading" });
  const [removing, setRemoving] = useState<string | null>(null);
  const [error, setError] = useState("");

  useEffect(() => {
    const controller = new AbortController();
    apiRequest<OAuthConnection[]>("/oauth/connections", { signal: controller.signal })
      .then((connections) => setLoad({ status: "ready", connections }))
      .catch((failure: unknown) => {
        if (!isAbortError(failure)) setLoad({ status: "error", message: errorMessage(failure) });
      });
    return () => controller.abort();
  }, []);

  const disconnect = async (connection: OAuthConnection) => {
    setRemoving(connection.id);
    setError("");
    try {
      await apiRequest<void>(`/oauth/connections/${encodeURIComponent(connection.id)}`, { method: "DELETE" });
      setLoad((current) =>
        current.status === "ready" ? { status: "ready", connections: current.connections.filter((c) => c.id !== connection.id) } : current,
      );
    } catch (failure) {
      setError(errorMessage(failure));
    } finally {
      setRemoving(null);
    }
  };

  return (
    <section id="connected-apps" aria-labelledby="connected-apps-title" className="pdx-card flex scroll-mt-6 flex-col gap-6">
      <SectionHeading
        kicker="Connected apps"
        title={<span id="connected-apps-title">Apps that can use your Playdex</span>}
        description="Assistants such as Claude or ChatGPT appear here after you allow them. Disconnecting one signs it out right away."
      />
      {load.status === "loading" ? (
        <p className="m-0 text-sm text-muted" role="status">
          Loading…
        </p>
      ) : load.status === "error" ? (
        <p role="alert" className="m-0 text-sm text-danger">
          {load.message}
        </p>
      ) : load.connections.length === 0 ? (
        <p className="m-0 text-sm text-muted">No apps are connected.</p>
      ) : (
        <ul className="m-0 grid list-none gap-3 p-0">
          {load.connections.map((connection) => (
            <li key={connection.id} className="pdx-tile flex flex-wrap items-center gap-4">
              <div className="min-w-0 flex-1">
                <p className="m-0 text-[15px] font-semibold [overflow-wrap:anywhere]">{connection.clientName}</p>
                <p className="m-0 mt-1 text-sm text-muted [overflow-wrap:anywhere]">
                  {hostOf(connection.clientId)} · connected {formatDateLabel(connection.connectedAt)}
                  {connection.lastUsedAt ? ` · last used ${formatDateLabel(connection.lastUsedAt)}` : ""}
                </p>
              </div>
              <button
                type="button"
                onClick={() => void disconnect(connection)}
                disabled={removing !== null}
                aria-label={`Disconnect ${connection.clientName}`}
                className="pdx-btn-outline"
              >
                {removing === connection.id ? "Disconnecting…" : "Disconnect"}
              </button>
            </li>
          ))}
        </ul>
      )}
      {error ? (
        <p role="alert" className="m-0 text-sm text-danger">
          {error}
        </p>
      ) : null}
    </section>
  );
}
