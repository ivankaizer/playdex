#!/usr/bin/env bash
# Creates, updates or removes the Dokploy preview stack for one pull request.
#
#   deploy/preview.sh deploy   # create the stack on first run, then redeploy; prints the URL
#   deploy/preview.sh destroy  # remove the stack and its volumes
#
# Required env: DOKPLOY_URL, DOKPLOY_API_KEY (CI secrets; ~/.env for local agents).
# PR_NUMBER and BRANCH default to the pull request of the checked-out branch (needs `gh`).
# See docs/preview-environments.md.
set -euo pipefail

: "${DOKPLOY_URL:?set DOKPLOY_URL (e.g. source ~/.env)}" "${DOKPLOY_API_KEY:?set DOKPLOY_API_KEY (e.g. source ~/.env)}"
DOKPLOY_ENVIRONMENT_ID="${DOKPLOY_ENVIRONMENT_ID:-UiuiXVqW5650lx-QhIG5R}" # project "playdex", env "previews"
DOKPLOY_SSH_KEY_ID="${DOKPLOY_SSH_KEY_ID:-nipG1gsiVfYwim4q8NUnl}"         # read-only deploy key
PREVIEW_DOMAIN="${PREVIEW_DOMAIN:-playdex.deployment.lat}"
REPO_SSH_URL="${REPO_SSH_URL:-git@github.com:ivankaizer/playdex.git}"
if [[ -z ${PR_NUMBER:-} || -z ${BRANCH:-} ]]; then
  read -r PR_NUMBER BRANCH <<<"$(gh pr view --json number,headRefName --jq '"\(.number) \(.headRefName)"')"
fi
COMPOSE_PATH="./docker-compose.preview.yml"
DEPLOY_TIMEOUT="${DEPLOY_TIMEOUT:-1500}"

name="pr-${PR_NUMBER}"
# DNS label: lowercase alphanumerics and dashes, at most 40 chars, no edge dashes.
slug=$(printf '%s' "$BRANCH" | tr '[:upper:]' '[:lower:]' | sed -E 's/[^a-z0-9]+/-/g; s/^-+//' | cut -c1-40 | sed -E 's/-+$//')
host="${slug:-$name}.${PREVIEW_DOMAIN}"
url="https://${host}"

api() { # api <GET|POST> <procedure> [json]
  local method=$1 proc=$2 body=${3:-}
  if [[ $method == GET ]]; then
    curl -fsS --retry 3 -H "x-api-key: ${DOKPLOY_API_KEY}" "${DOKPLOY_URL}/api/${proc}"
  else
    curl -fsS --retry 3 -H "x-api-key: ${DOKPLOY_API_KEY}" -H "Content-Type: application/json" \
      -d "$body" "${DOKPLOY_URL}/api/${proc}"
  fi
}

find_compose_id() {
  api GET "environment.one?environmentId=${DOKPLOY_ENVIRONMENT_ID}" |
    jq -r --arg n "$name" '[.compose[]? | select(.name == $n)][0].composeId // empty'
}

latest_deployment() { # prints "<id> <status>"
  api GET "deployment.allByCompose?composeId=$1" |
    jq -r 'sort_by(.createdAt) | last | if . then "\(.deploymentId) \(.status)" else "" end'
}

create_compose() {
  local id
  id=$(api POST compose.create "$(jq -nc --arg n "$name" --arg e "$DOKPLOY_ENVIRONMENT_ID" \
    '{name: $n, environmentId: $e, composeType: "docker-compose", sourceType: "git",
      description: "Preview for pull request"}')" | jq -r .composeId)

  api POST compose.update "$(jq -nc --arg id "$id" --arg url "$REPO_SSH_URL" --arg b "$BRANCH" \
    --arg key "$DOKPLOY_SSH_KEY_ID" --arg path "$COMPOSE_PATH" \
    '{composeId: $id, sourceType: "git", customGitUrl: $url, customGitBranch: $b,
      customGitSSHKeyId: $key, composePath: $path, autoDeploy: false, isolatedDeployment: true,
      env: "COMPOSE_PARALLEL_LIMIT=1"}')" >/dev/null

  for route in "ui 5056 /" "app 3000 /api"; do
    read -r service port path <<<"$route"
    api POST domain.create "$(jq -nc --arg id "$id" --arg h "$host" --arg s "$service" \
      --argjson p "$port" --arg path "$path" \
      '{composeId: $id, host: $h, serviceName: $s, port: $p, path: $path, stripPath: false,
        https: true, certificateType: "letsencrypt", domainType: "compose"}')" >/dev/null
  done
  echo "$id"
}

deploy() {
  local id previous deployment status
  id=$(find_compose_id)
  if [[ -z $id ]]; then
    id=$(create_compose)
    echo "Created Dokploy compose ${name} (${id})" >&2
  fi

  previous=$(latest_deployment "$id" | cut -d' ' -f1)
  api POST compose.deploy "$(jq -nc --arg id "$id" --arg t "PR #${PR_NUMBER} @ ${GITHUB_SHA:-manual}" \
    '{composeId: $id, title: $t}')" >/dev/null
  echo "Deploy queued for ${name}; waiting up to ${DEPLOY_TIMEOUT}s" >&2

  local deadline=$((SECONDS + DEPLOY_TIMEOUT))
  while ((SECONDS < deadline)); do
    sleep 10
    read -r deployment status <<<"$(latest_deployment "$id")" || true
    [[ -z ${deployment:-} || ${deployment} == "$previous" ]] && continue
    case $status in
      done) break ;;
      error) echo "Dokploy deployment ${deployment} failed; see ${DOKPLOY_URL}" >&2; exit 1 ;;
    esac
  done
  [[ ${status:-} == done ]] || { echo "Timed out waiting for deployment" >&2; exit 1; }

  # Traefik needs a moment to pick up routes and issue the certificate.
  local api_status=000
  for _ in $(seq 1 30); do
    api_status=$(curl -s -o /dev/null -w '%{http_code}' --max-time 10 "${url}/api/" || true)
    if curl -fs -o /dev/null --max-time 10 "${url}/" && [[ $api_status =~ ^[234] ]]; then
      echo "$url"
      return
    fi
    sleep 5
  done
  echo "Deployed, but ${url} is not answering (API returned ${api_status})" >&2
  exit 1
}

destroy() {
  local id
  id=$(find_compose_id)
  if [[ -z $id ]]; then
    echo "No preview ${name} to remove" >&2
    return
  fi
  api POST compose.delete "$(jq -nc --arg id "$id" '{composeId: $id, deleteVolumes: true}')" >/dev/null
  echo "Removed preview ${name}" >&2
}

case "${1:-}" in
  deploy) deploy ;;
  destroy) destroy ;;
  *) echo "usage: $0 deploy|destroy" >&2; exit 2 ;;
esac
