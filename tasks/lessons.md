# Lessons

## 2026-03-23 - Normalize DB IDs in tests before encoding/comparing
- Pattern: Integration tests that read bigint IDs from PostgreSQL can receive string values at runtime even when TypeScript types say `number`.
- Failure mode: Passing raw string IDs into helpers like `encodeGameId(...)` silently produces invalid/empty values; numeric equality assertions also fail.
- Rule:
  - Normalize any DB `id` field with `Number(...)` and validate positive integer before use in tests.
  - Do this at helper boundaries (`create*` helpers and assertion mapping), not ad hoc in individual assertions.

## 2026-05-31 - Keep SDD artifacts in numbered feature directories
- Pattern: A requirements draft was written to the repository root even though feature specifications need stable, isolated locations.
- Failure mode: Root-level `requirements.md`, `design.md`, or `tasks.md` files lose feature identity and allow later SDD stages to drift across unrelated work.
- Rule:
  - Create each feature under `specs/<NNN>-<feature-slug>/` using a three-digit sequence and kebab-case slug.
  - Keep `requirements.md`, `design.md`, and `tasks.md` together in that same directory.
  - Allocate the next available sequence for a new feature; reuse a directory only when revising the same feature.

## 2026-05-31 - Execute an approved SDD feature end to end
- Pattern: The implementation workflow stopped after each subtask and required repeated `next` messages.
- Failure mode: Artificial review gates interrupt autonomous execution even after the user has approved the numbered feature plan.
- Rule:
  - Treat `$implement <NNN>` as a request to complete every remaining task in `specs/<NNN>-<feature-slug>/tasks.md`.
  - Track and verify tasks individually, but continue automatically after each verified task.
  - Ask for review only after final feature verification or when a genuine blocker requires user input.
