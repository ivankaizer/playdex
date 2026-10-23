#!/usr/bin/env bash
# Claude Code PostToolUse hook (see .claude/settings.json): formats the one file an Edit, Write or MultiEdit just
# changed, the way `bun run format` and `dotnet format` would. It must never block the agent, so it always exits 0.

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)" || exit 0
input="$(cat)"

if command -v jq >/dev/null 2>&1; then
  file="$(printf '%s' "$input" | jq -r '.tool_input.file_path // empty' 2>/dev/null)"
elif command -v bun >/dev/null 2>&1; then
  file="$(printf '%s' "$input" | bun -e 'const d = JSON.parse(await Bun.stdin.text()); console.log(d.tool_input?.file_path ?? "")' 2>/dev/null)"
fi
[ -n "${file:-}" ] && [ -f "$file" ] || exit 0

cd "$root" || exit 0
case "$file" in
  *.js | *.mjs | *.cjs | *.ts | *.tsx | *.json | *.jsonc | *.css)
    # biome.json decides which files are in scope; anything else is skipped. Needs `bun install` at the root.
    if [ -d node_modules/@biomejs/biome ] && command -v bun >/dev/null 2>&1; then
      bun run biome check --write --no-errors-on-unmatched --files-ignore-unknown=true "$file" >/dev/null 2>&1
    fi
    ;;
  *.cs)
    if command -v dotnet >/dev/null 2>&1; then
      dotnet format whitespace --folder --include "$file" >/dev/null 2>&1
    fi
    ;;
esac
exit 0
