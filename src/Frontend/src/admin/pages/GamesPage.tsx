import { useCallback, useEffect, useRef, useState, type FormEvent } from "react";
import { errorMessage } from "@/lib/api";
import { adminApi, isAbortError, type GameSummary } from "../api";
import { useNotifications } from "../components/Notifications";
import { Alert, Button, Card, Checkbox, Field, PageHeading, ProgressBar, Select, Table, Td, TextInput, Th, Toolbar } from "../components/ui";
import { gameFormFor, gameUpdateFor, mergeValidationError, type GameForm } from "../logic";
import { cx } from "@/lib/cx";

export function GamesPage() {
  const notify = useNotifications();
  const [query, setQuery] = useState("");
  const [games, setGames] = useState<GameSummary[]>([]);
  const [selected, setSelected] = useState<GameSummary | null>(null);
  const [form, setForm] = useState<GameForm | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [sourceId, setSourceId] = useState("");
  const [targetId, setTargetId] = useState("");

  const queryRef = useRef(query);
  queryRef.current = query;
  const controllerRef = useRef<AbortController | null>(null);

  /** Blank searches list the 20 most recently updated games, like the Blazor page. */
  const search = useCallback(async () => {
    controllerRef.current?.abort();
    const controller = new AbortController();
    controllerRef.current = controller;
    setLoading(true);
    setSelected(null);
    setForm(null);
    try {
      const result = await adminApi.games(queryRef.current, { signal: controller.signal });
      if (controller.signal.aborted) return;
      setGames(result);
      setError(null);
    } catch (caught) {
      if (controller.signal.aborted || isAbortError(caught)) return;
      setError(errorMessage(caught));
    } finally {
      if (controllerRef.current === controller) setLoading(false);
    }
  }, []);

  useEffect(() => {
    void search();
    return () => controllerRef.current?.abort();
  }, [search]);

  const submitSearch = (event: FormEvent) => {
    event.preventDefault();
    if (!loading) void search();
  };

  const select = (game: GameSummary) => {
    setSelected(game);
    setForm(gameFormFor(game));
    setActionError(null);
  };

  const save = async () => {
    if (selected === null || form === null || saving) return;
    setSaving(true);
    try {
      await adminApi.updateGame(selected.id, gameUpdateFor(selected, form));
      notify.success("Game updated", "The game metadata was saved.");
      setActionError(null);
      await search();
    } catch (caught) {
      const message = errorMessage(caught);
      setActionError(message);
      notify.error("Game update failed", message);
    } finally {
      setSaving(false);
    }
  };

  const merge = async () => {
    const source = sourceId === "" ? null : Number(sourceId);
    const target = targetId === "" ? null : Number(targetId);
    const invalid = mergeValidationError(source, target);
    if (saving || invalid !== null) {
      setActionError(invalid ?? "Select two different games.");
      return;
    }
    if (!window.confirm("This permanently removes the source game and reassigns its relationships. Continue?")) return;
    setSaving(true);
    try {
      await adminApi.mergeGames(source!, target!);
      notify.success("Games merged", "The source game was merged into the target.");
      setSourceId("");
      setTargetId("");
      setActionError(null);
      await search();
    } catch (caught) {
      const message = errorMessage(caught);
      setActionError(message);
      notify.error("Merge failed", message);
    } finally {
      setSaving(false);
    }
  };

  return (
    <>
      <PageHeading>Games</PageHeading>
      <form onSubmit={submitSearch}>
        <Toolbar>
          <TextInput
            className="sm:w-96"
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            placeholder="Title, capsule URL, or game identifier"
            aria-label="Search games"
          />
          <Button type="submit" variant="primary" disabled={loading}>
            Search
          </Button>
        </Toolbar>
      </form>
      {actionError !== null ? <Alert tone="danger">{actionError}</Alert> : null}
      {error !== null ? (
        <Alert tone="danger">{error}</Alert>
      ) : loading ? (
        <ProgressBar />
      ) : games.length === 0 ? (
        <Alert>No games found.</Alert>
      ) : (
        <>
          <p className="mb-2 text-sm text-slate-500 dark:text-slate-400">Select a row to edit the game.</p>
          <Table>
            <thead>
              <tr>
                <Th className="w-20">Id</Th>
                <Th>Title</Th>
                <Th>Capsule URL</Th>
                <Th className="w-28 text-center">Title locked</Th>
                <Th className="w-28 text-center">Image locked</Th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100 dark:divide-slate-800">
              {games.map((game) => (
                <tr
                  key={game.id}
                  tabIndex={0}
                  aria-selected={selected?.id === game.id}
                  onClick={() => select(game)}
                  onKeyDown={(event) => {
                    if (event.key === "Enter" || event.key === " ") {
                      event.preventDefault();
                      select(game);
                    }
                  }}
                  className={cx(
                    "cursor-pointer focus:outline-none focus-visible:ring-2 focus-visible:ring-emerald-500",
                    selected?.id === game.id
                      ? "bg-emerald-50 dark:bg-emerald-950/40"
                      : "bg-white hover:bg-slate-50 dark:bg-slate-900 dark:hover:bg-slate-800",
                  )}
                >
                  <Td>{game.id}</Td>
                  <Td>{game.title}</Td>
                  <Td className="max-w-xs break-all">{game.capsuleUrl}</Td>
                  <Td className="text-center">{game.titleLocked ? "Yes" : "No"}</Td>
                  <Td className="text-center">{game.capsuleUrlLocked ? "Yes" : "No"}</Td>
                </tr>
              ))}
            </tbody>
          </Table>

          {selected !== null && form !== null ? (
            <Card className="mt-6">
              <h2 className="mb-3 text-lg font-semibold">Edit game</h2>
              <div className="grid gap-4 md:grid-cols-2">
                <Field label="Title" htmlFor="game-title">
                  <TextInput
                    id="game-title"
                    value={form.title}
                    placeholder="Title"
                    onChange={(event) => setForm({ ...form, title: event.target.value })}
                  />
                </Field>
                <Field
                  label="Capsule URL"
                  htmlFor="game-capsule"
                  hint={
                    <small id="game-capsule-hint" className="text-slate-500 dark:text-slate-400">
                      Leave empty to remove the image.
                    </small>
                  }
                >
                  <TextInput
                    id="game-capsule"
                    value={form.capsuleUrl ?? ""}
                    placeholder="Image URL"
                    aria-describedby="game-capsule-hint"
                    onChange={(event) => setForm({ ...form, capsuleUrl: event.target.value })}
                  />
                </Field>
              </div>
              <div className="mt-3 flex flex-wrap gap-6">
                <Checkbox label="Title locked" checked={form.titleLocked} onChange={(titleLocked) => setForm({ ...form, titleLocked })} />
                <Checkbox
                  label="Image locked"
                  checked={form.capsuleUrlLocked}
                  onChange={(capsuleUrlLocked) => setForm({ ...form, capsuleUrlLocked })}
                />
              </div>
              <div className="mt-4 flex gap-2">
                <Button variant="primary" onClick={() => void save()} disabled={saving}>
                  Save
                </Button>
                <Button
                  onClick={() => {
                    setSelected(null);
                    setForm(null);
                  }}
                  disabled={saving}
                >
                  Cancel
                </Button>
              </div>
            </Card>
          ) : null}

          {games.length > 1 ? (
            <Card className="mt-6">
              <h2 className="mb-3 text-lg font-semibold">Merge games</h2>
              <div className="grid gap-4 md:grid-cols-2">
                <Field label="Source game" htmlFor="merge-source">
                  <Select id="merge-source" value={sourceId} onChange={(event) => setSourceId(event.target.value)}>
                    <option value="">Source game</option>
                    {games.map((game) => (
                      <option key={game.id} value={game.id}>
                        {game.title} (#{game.id})
                      </option>
                    ))}
                  </Select>
                </Field>
                <Field label="Target game" htmlFor="merge-target">
                  <Select id="merge-target" value={targetId} onChange={(event) => setTargetId(event.target.value)}>
                    <option value="">Target game</option>
                    {games.map((game) => (
                      <option key={game.id} value={game.id}>
                        {game.title} (#{game.id})
                      </option>
                    ))}
                  </Select>
                </Field>
              </div>
              <p className="mt-2 text-sm text-slate-500 dark:text-slate-400">
                The source game is deleted; its sessions, identities, ratings, ignores, connections and AI drafts move to the target.
              </p>
              <div className="mt-4">
                <Button variant="danger" onClick={() => void merge()} disabled={saving}>
                  Merge
                </Button>
              </div>
            </Card>
          ) : null}
        </>
      )}
    </>
  );
}
