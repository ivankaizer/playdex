# Lessons

## 2026-03-23 - Normalize DB IDs in tests before encoding/comparing
- Pattern: Integration tests that read bigint IDs from PostgreSQL can receive string values at runtime even when TypeScript types say `number`.
- Failure mode: Passing raw string IDs into helpers like `encodeGameId(...)` silently produces invalid/empty values; numeric equality assertions also fail.
- Rule:
  - Normalize any DB `id` field with `Number(...)` and validate positive integer before use in tests.
  - Do this at helper boundaries (`create*` helpers and assertion mapping), not ad hoc in individual assertions.
