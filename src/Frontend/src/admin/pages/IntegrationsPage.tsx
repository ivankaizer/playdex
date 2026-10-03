import { useCallback, useEffect, useRef, useState } from "react";
import { errorMessage } from "@/lib/api";
import { adminApi, isAbortError, type IntegrationSummary, type PlatformSummary } from "../api";
import { useNotifications } from "../components/Notifications";
import { Alert, Button, Card, Checkbox, PageHeading, Pager, ProgressBar, rowClass, SectionHeading, Select, Table, Td, TextInput, Th, Toolbar } from "../components/ui";
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

  return (
    <>
      <PageHeading>Integrations</PageHeading>
      {error !== null ? (
        <>
          <Alert tone="danger">{error}</Alert>
          <Button onClick={() => void refresh()} disabled={loading}>
            Retry
          </Button>
        </>
      ) : loading && integrations.length === 0 ? (
        <ProgressBar />
      ) : (
        <>
          <Toolbar>
            <Select
              className="sm:w-56"
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
            <Button onClick={() => void refresh()} disabled={loading}>
              Refresh
            </Button>
          </Toolbar>
          {loading ? <ProgressBar /> : null}
          {visible.length === 0 ? (
            <Alert>No integrations found.</Alert>
          ) : (
            <Table>
              <thead>
                <tr>
                  <Th>Owner</Th>
                  <Th>Platform</Th>
                  <Th>Account</Th>
                  <Th className="w-24 text-center">Active</Th>
                  <Th>API key</Th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100 dark:divide-slate-800">
                {paged.rows.map((integration) => (
                  <tr key={integration.id} className={rowClass}>
                    <Td>{integration.email}</Td>
                    <Td>{integration.platform}</Td>
                    <Td>{integration.accountId}</Td>
                    <Td className="text-center">{integration.active ? "Yes" : "No"}</Td>
                    <Td>
                      <div className="flex items-center gap-2">
                        <span className="break-all font-mono text-xs">{integration.apiKey ?? "Not set"}</span>
                        <Button className="shrink-0" onClick={() => beginEdit(integration)}>
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

          {editing !== null && form !== null ? (
            <Card className="mt-6">
              <h2 className="mb-3 text-lg font-semibold">Edit credentials for {editing.email}</h2>
              <div className="flex flex-col gap-4">
                <div className="flex flex-col gap-2">
                  <Checkbox
                    label="Replace or clear API key"
                    checked={form.replaceApiKey}
                    onChange={(replaceApiKey) => setForm({ ...form, replaceApiKey })}
                  />
                  {form.replaceApiKey ? (
                    <TextInput
                      value={form.apiKey}
                      onChange={(event) => setForm({ ...form, apiKey: event.target.value })}
                      placeholder="API key (empty clears it)"
                      aria-label="API key"
                    />
                  ) : null}
                </div>
                <div className="flex flex-col gap-2">
                  <Checkbox
                    label="Change account identifier"
                    checked={form.changeAccountId}
                    onChange={(changeAccountId) => setForm({ ...form, changeAccountId })}
                  />
                  {form.changeAccountId ? (
                    <TextInput
                      value={form.accountId}
                      onChange={(event) => setForm({ ...form, accountId: event.target.value })}
                      placeholder="Account identifier"
                      aria-label="Account identifier"
                    />
                  ) : null}
                </div>
                <div className="flex gap-2">
                  <Button
                    variant="primary"
                    onClick={() => void saveCredentials()}
                    disabled={loading || (!form.replaceApiKey && !form.changeAccountId)}
                  >
                    Save credentials
                  </Button>
                  <Button
                    onClick={() => {
                      setEditing(null);
                      setForm(null);
                    }}
                    disabled={loading}
                  >
                    Cancel
                  </Button>
                </div>
              </div>
            </Card>
          ) : null}

          <SectionHeading>Platform controls</SectionHeading>
          <div className="flex flex-wrap gap-2">
            {platforms.map((platform) => (
              <Button
                key={platform.key}
                variant={platform.enabled ? "danger" : "primary"}
                onClick={() => void togglePlatform(platform)}
                disabled={loading}
                title={`${platform.active} of ${platform.total} active`}
              >
                {platform.enabled ? `Disable ${platform.key}` : `Enable ${platform.key}`}
              </Button>
            ))}
          </div>
        </>
      )}
    </>
  );
}
