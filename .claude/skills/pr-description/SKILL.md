---
name: pr-description
description: Write or rewrite a pull request description with a TL;DR, a mermaid graph of what changed, criticality and reversibility tables, testing and after-merge steps. Use when opening a PR, when asked to write or improve a PR/MR description, or to update an existing PR's body.
---

# PR description

A reviewer should know from the first screen what changed, how much it can hurt, and how to undo it. Write for
someone who has not seen the work, and say only what the diff, the commits and the test runs support.

## 1. Gather

- The base branch (usually `main`) and the head branch; the PR number if one exists (`gh pr view --json number,body`).
- `git log --oneline <base>..<head>` and `git diff --stat <base>...<head>`, then read the diff itself. Group the
  changes by component (a module, a service, a background job, a page, a config file), not by file.
- What was actually run: build, tests (with counts), format, manual or end-to-end checks. Take it from this
  conversation or rerun it. Anything not verified is said to be not verified.
- Settings, env vars, external services or resources the change adds, and steps someone must take after merging.

## 2. Write it in this order

### TL;DR

Two to four sentences: what changes, what users or operators will notice, and the overall risk (including "nothing
happens until X is set" or "no migrations, safe to revert" when true). No headings, no lists.

### What changed

A mermaid `flowchart LR` of the components the PR touches and the data or calls between them:

- Nodes are components and actors (users, admins, external services), not files. Group related nodes in
  `subgraph`s (the app, new infrastructure, external services).
- Label edges with what flows: "sessions per account", "Telegram", "HTTP 202".
- Mark nodes with these classes and put the legend `🟩 added · 🟨 changed · 🟥 removed` under the graph:
  ```
  classDef added fill:#d1f7d6,stroke:#2da44e,color:#000
  classDef removed fill:#ffd7d5,stroke:#cf222e,color:#000,stroke-dasharray:4
  classDef changed fill:#fff4c2,stroke:#bf8700,color:#000
  ```
- A removed flow is a dotted edge labelled `✗ removed` (`A -. "x ✗ removed" .-> B`).
- Keep it under about 15 nodes; leave out what the PR does not touch. Quote labels that contain `/`, `(` or `:`, and
  use `<br/>` for line breaks.

Below the graph, a table with one row per commit (short hash, one line on what it does). For a single-commit PR,
three to five bullets instead.

### Criticality

A table `| Area | Level | Why |`, one row per area from the graph, most critical first:

| Level | When |
|---|---|
| 🔴 **High** | Can lose or corrupt data, break sign-in or billing, or take the service down; a migration that rewrites data. |
| 🟠 **Medium** | Behaviour users will notice, or code on a hot path (every request, every poll) that could fail. |
| 🟡 **Low–medium** | New code on a hot path that is guarded so its failure cannot break the path. |
| 🟢 **Low** | Off by default, additive, admin-only, or removes something nothing uses (say how you know it is unused). |
| ⚪ **None** | Explicitly untouched areas a reviewer would worry about, e.g. "Database: no migrations". |

The "Why" says what breaks if the change is wrong, and what covers it (tests, guards, flags).

### Reversibility

A table `| Change | Revert | Notes |`. For every change, how it is undone: `✅ git revert`, `✅ unset X`,
`⚠️ one-way`, `❌ irreversible`. Look for these on purpose; they are what a plain revert misses:

- Migrations: schema changes, data rewritten or deleted, columns dropped.
- Data or messages that cannot be taken back: emails or notifications sent, queued items dropped, files deleted.
- Resources created outside the repo: dashboards, alert rules, monitors, buckets, webhooks, DNS, third-party
  settings. Say how to remove them and what happens if they are left behind after a revert.
- Removed endpoints, settings or fields that clients or deployments may still use.
- Config that must be changed together with the code.

### Testing

Bullets: commands run and their results (counts), end-to-end or manual checks and what they showed. Mark anything not
verified with ⚠️, e.g. "not tested against production Grafana", "Windows code not compiled here".

### After merge

Numbered steps only if someone must act (set env vars, run a script, create a token). Leave the section out
otherwise.

End the body with the attribution line the session asks for, if any.

## 3. Apply

Write the body to a file in the scratchpad, then `gh pr create --base <base> --head <head> --title "<conventional
commit style title>" --body-file <file>`, or `gh pr edit <number> --body-file <file>` for an existing PR. Give the
user the PR link and say which parts you could not verify.
