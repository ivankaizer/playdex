#!/usr/bin/env bash
set -euo pipefail

REFERENCE_COMMIT="7bf8e76"
: "${BUN_API_BASE_URL:=http://127.0.0.1:3000}"
: "${DOTNET_API_BASE_URL:=http://127.0.0.1:5100}"

processes=()
cleanup() {
  if ((${#processes[@]})); then
    kill "${processes[@]}" 2>/dev/null || true
  fi
}
trap cleanup EXIT

run_optional() {
  local command="$1"
  if [[ -n "$command" ]]; then
    bash -lc "$command"
  fi
}

start_optional() {
  local command="$1"
  if [[ -n "$command" ]]; then
    bash -lc "$command" &
    processes+=("$!")
  fi
}

run_optional "${BUN_DATABASE_RESET_COMMAND:-}"
run_optional "${DOTNET_DATABASE_RESET_COMMAND:-}"
start_optional "${BUN_API_START_COMMAND:-}"
start_optional "${DOTNET_API_START_COMMAND:-}"

export BUN_API_BASE_URL DOTNET_API_BASE_URL
echo "Comparing Bun ${REFERENCE_COMMIT} at ${BUN_API_BASE_URL} with .NET at ${DOTNET_API_BASE_URL}"
dotnet test apps/api-dotnet/Playdex.ContractTests/Playdex.ContractTests.csproj \
  --configuration Release "$@"
