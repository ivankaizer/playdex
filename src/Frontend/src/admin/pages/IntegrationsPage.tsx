import { useCallback, useEffect, useRef, useState } from "react";
import { errorMessage } from "@/lib/api";
import { adminApi, isAbortError, type IntegrationSummary, type PlatformSummary } from "../api";
import { useNotifications } from "../components/Notifications";
import { platformColor } from "@/lib/platforms";
import { Alert, Badge, Button, Checkbox, Modal, Note, Pager, Panel, ProgressBar, rowClass, Select, Table, Td, TextInput, Th, Toolbar } from "../components/ui";
import { credentialFormFor, integrationUpdateFor, pageOf, type CredentialForm } from "../logic";

const PageSize = 25;

export function IntegrationsPage() {
  const notify = useNotifications();
  const [integrations, setIntegrations] = useState<IntegrationSummary[]>([]);
  const [platforms, setPlatforms] = useState<PlatformSummary[]>([]);
  const [filter, setFilter] = useState("");
  const [page, setPage] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState<IntegrationSummary | null>(null);
  const [form, setForm] = useState<CredentialForm | null>(null);
  const controllerRef = useRef<AbortController | null>(null);

  const refresh = useCallback(async () => {
    controllerRef.current?.abort();
    const controller = new AbortController();
    controllerRef.current = controller;
    setLoading(true);
    try {
      const [rows, summaries] = await Promise.all([
        adminApi.integrations({ signal: controller.signal }),
        adminApi.platforms({ signal: controller.signal }),
      ]);
      if (controller.signal.aborted) return;
      setIntegrations(rows);
      setPlatforms(summaries);
      setError(null);
    } catch (caught) {
      if (controller.signal.aborted || isAbortError(caught)) return;
      setError(errorMessage(caught));
    } finally {
      if (controllerRef.current === controller) setLoading(false);
    }
  }, []);

  useEffect(() => {
    void refresh();
    return () => controllerRef.current?.abort();
  }, [refresh]);

  const visible = filter === "" ? integrations : integrations.filter((row) => row.platform === filter);
  const paged = pageOf(visible, page, PageSize);

  const togglePlatform = async (platform: PlatformSummary) => {
    if (loading) return;
    const enabled = !platform.enabled;
    if (!window.confirm(`Change all ${platform.total} integrations for ${platform.key}?`)) return;
    setLoading(true);
    try {
      const result = await adminApi.setPlatformEnabled(platform.key, enabled);
      notify.success("Platform updated", `Updated ${result.updated} integrations.`);
      await refresh();
    } catch (caught) {
      const message = errorMessage(caught);
      setError(message);
      notify.error("Platform update failed", message);
      setLoading(false);
    }
  };

  const beginEdit = (integration: IntegrationSummary) => {
    setEditing(integration);
    setForm(credentialFormFor(integration));
  };

  const saveCredentials = async () => {
    if (editing === null || form === null || loading) return;
    const update = integrationUpdateFor(form);
    if (update === null) return;
    setLoading(true);
    try {
      await adminApi.updateIntegration(editing.id, update);
      notify.success("Credentials updated", "The integration was refreshed.");
      setEditing(null);
      setForm(null);
      await refresh();
    } catch (caught) {
      const message = errorMessage(caught);
      setError(message);
      notify.error("Credential update failed", message);
      setLoading(false);
    }
  };

  const ready = error === null && !(loading && integrations.length === 0);

  const closeEditor = () => {
    setEditing(null);
    setForm(null);
  };

  return (
    <>
      <Panel
        label="Integrations"
        kicker="Connected accounts"
        title="Integrations"
        actions={
          ready ? (
            <Button onClick={() => void refresh()} disabled={loading}>
              Refresh
            </Button>
          ) : undefined
        }
      >
        {error !== null ? (
          <div className="flex flex-col items-start gap-3">
            <Alert tone="danger">{error}</Alert>
            <Button onClick={() => void refresh()} disabled={loading}>
              Retry
            </Button>
          </div>
        ) : loading && integrations.length === 0 ? (
          <ProgressBar />
        ) : (
          <>
            <Toolbar>
              <Select
                className="sm:!w-56"
                value={filter}
                onChange={(event) => {
                  setFilter(event.target.value);
                  setPage(0);
                }}
                aria-label="Filter platform"
              >
                <option value="">All platforms</option>
                {platforms.map((platform) => (
                  <option key={platform.key} value={platform.key}>
                    {platform.key}
                  </option>
                ))}
              </Select>
            </Toolbar>
            {loading ? <ProgressBar /> : null}
            {visible.length === 0 ? (
              <Alert>No integrations found.</Alert>
            ) : (
              <Table label="Integrations table">
                <thead>
                  <tr>
                    <Th>Owner</Th>
                    <Th>Platform</Th>
                    <Th>Account</Th>
                    <Th className="w-24 text-center">Active</Th>
                    <Th>API key</Th>
                  </tr>
                </thead>
                <tbody>
                  {paged.rows.map((integration) => (
                    <tr key={integration.id} className={rowClass}>
                      <Td className="break-all">{integration.email}</Td>
                      <Td>
                        <span className="inline-flex items-center gap-2 whitespace-nowrap">
                          <span
                            aria-hidden="true"
                            className="h-2 w-2 shrink-0 rounded-full"
                            style={{ backgroundColor: platformColor(integration.platform) }}
                          />
                          {integration.platform}
                        </span>
                      </Td>
                      <Td className="break-all">{integration.accountId}</Td>
                      <Td className="text-center">
                        {integration.active ? <Badge tone="success">Yes</Badge> : <Badge>No</Badge>}
                      </Td>
                      <Td>
                        <div className="flex min-w-[220px] items-center justify-between gap-3">
                          <span className="break-all font-mono text-xs text-ink-2">{integration.apiKey ?? "Not set"}</span>
                          <Button variant="accent" size="sm" className="shrink-0" onClick={() => beginEdit(integration)}>
                            Edit
                          </Button>
                        </div>
                      </Td>
                    </tr>
                  ))}
                </tbody>
              </Table>
            )}
            {paged.pages > 1 ? (
              <Pager
                label={`${visible.length} integrations · Page ${paged.page + 1} of ${paged.pages}`}
                page={paged.page}
                hasNext={paged.page + 1 < paged.pages}
                onPage={setPage}
              />
            ) : null}
          </>
        )}
      </Panel>

      {ready ? (
        <Panel label="Platform controls" kicker="Polling" title="Platform controls">
          <Note>Turn polling on or off for every integration on a platform at once.</Note>
          <div className="grid gap-3 [grid-template-columns:repeat(auto-fill,minmax(min(240px,100%),1fr))]">
            {platforms.map((platform) => (
              <div key={platform.key} className="pdx-tile flex flex-col gap-3 !px-5 !py-4">
                <div className="flex items-center justify-between gap-3">
                  <span className="inline-flex min-w-0 items-center gap-2 font-semibold">
                    <span
                      aria-hidden="true"
                      className="h-2.5 w-2.5 shrink-0 rounded-full"
                      style={{ backgroundColor: platformColor(platform.key, platform.name) }}
                    />
                    <span className="truncate">{platform.key}</span>
                  </span>
                  {platform.enabled ? <Badge tone="success">Enabled</Badge> : <Badge>Disabled</Badge>}
                </div>
                <p className="pdx-num text-[13px] text-muted">
                  {platform.active} of {platform.total} active
                </p>
                <Button
                  variant={platform.enabled ? "danger" : "primary"}
                  size="sm"
                  onClick={() => void togglePlatform(platform)}
                  disabled={loading}
                  title={`${platform.active} of ${platform.total} active`}
                >
                  {platform.enabled ? `Disable ${platform.key}` : `Enable ${platform.key}`}
                </Button>
              </div>
            ))}
          </div>
        </Panel>
      ) : null}

      {ready && editing !== null && form !== null ? (
        <Modal kicker="Credentials" title={`Edit credentials for ${editing.email}`} busy={loading} onClose={closeEditor}>
          <div className="flex flex-col gap-4">
            <div className="flex flex-col gap-2.5">
              <Checkbox
                label="Replace or clear API key"
                checked={form.replaceApiKey}
                onChange={(replaceApiKey) => setForm({ ...form, replaceApiKey })}
              />
              {form.replaceApiKey ? (
                <TextInput
                  className="!h-11"
                  value={form.apiKey}
                  onChange={(event) => setForm({ ...form, apiKey: event.target.value })}
                  placeholder="API key (empty clears it)"
                  aria-label="API key"
                />
              ) : null}
            </div>
            <div className="flex flex-col gap-2.5">
              <Checkbox
                label="Change account identifier"
                checked={form.changeAccountId}
                onChange={(changeAccountId) => setForm({ ...form, changeAccountId })}
              />
              {form.changeAccountId ? (
                <TextInput
                  className="!h-11"
                  value={form.accountId}
                  onChange={(event) => setForm({ ...form, accountId: event.target.value })}
                  placeholder="Account identifier"
                  aria-label="Account identifier"
                />
              ) : null}
            </div>
          </div>
          <div className="mt-1 flex flex-wrap justify-end gap-2.5">
            <Button className="!h-11" onClick={closeEditor} disabled={loading}>
              Cancel
            </Button>
            <Button
              variant="primary"
              className="!h-11 !px-[22px]"
              onClick={() => void saveCredentials()}
              disabled={loading || (!form.replaceApiKey && !form.changeAccountId)}
            >
              Save credentials
            </Button>
          </div>
        </Modal>
      ) : null}
    </>
  );
}
