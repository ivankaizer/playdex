#!/usr/bin/env bash
# Pushes the Playdex dashboards and alert rules to a Grafana stack (Grafana Cloud or self-hosted). Safe to re-run:
# everything is created or updated in place, by uid.
#
#   GRAFANA_URL                 https://<stack>.grafana.net
#   GRAFANA_TOKEN               a service account token with the Editor role
#   PROMETHEUS_DATASOURCE_UID   the metrics data source the alerts query (default: grafanacloud-prom, Grafana Cloud's)
#   TELEGRAM_BOT_TOKEN, TELEGRAM_CHAT_ID
#                               optional: create or update the "playdex-admin" contact point to send alerts to
#                               that chat. Without them the contact point must already exist.
#   CONTACT_POINT               the contact point alerts go to (default: playdex-admin)
#   FOLDER_UID                  the folder everything goes in (default: playdex)
set -euo pipefail

: "${GRAFANA_URL:?set GRAFANA_URL}"
: "${GRAFANA_TOKEN:?set GRAFANA_TOKEN}"
PROMETHEUS_DATASOURCE_UID="${PROMETHEUS_DATASOURCE_UID:-grafanacloud-prom}"
CONTACT_POINT="${CONTACT_POINT:-playdex-admin}"
FOLDER_UID="${FOLDER_UID:-playdex}"
here="$(cd "$(dirname "$0")" && pwd)"

# api METHOD PATH [BODY]: prints the response body, fails on an HTTP error.
api() {
  local response status
  response="$(curl -sS -X "$1" "${GRAFANA_URL%/}$2" \
    -H "Authorization: Bearer $GRAFANA_TOKEN" \
    -H "Content-Type: application/json" \
    -H "X-Disable-Provenance: true" \
    ${3:+--data-binary "$3"} \
    -w '\n%{http_code}')"
  status="${response##*$'\n'}"
  if [[ "$status" -ge 400 ]]; then
    echo "$1 $2 failed ($status): ${response%$'\n'*}" >&2
    return 1
  fi
  printf '%s' "${response%$'\n'*}"
}

exists() { api GET "$1" >/dev/null 2>&1; }

exists "/api/datasources/uid/$PROMETHEUS_DATASOURCE_UID" ||
  { echo "No data source with uid '$PROMETHEUS_DATASOURCE_UID'; set PROMETHEUS_DATASOURCE_UID." >&2; exit 1; }

if ! exists "/api/folders/$FOLDER_UID"; then
  api POST /api/folders "$(jq -n --arg uid "$FOLDER_UID" '{uid: $uid, title: "Playdex"}')" >/dev/null
  echo "Created folder $FOLDER_UID"
fi

if [[ -n "${TELEGRAM_BOT_TOKEN:-}" && -n "${TELEGRAM_CHAT_ID:-}" ]]; then
  contact="$(jq -n --arg name "$CONTACT_POINT" --arg token "$TELEGRAM_BOT_TOKEN" --arg chat "$TELEGRAM_CHAT_ID" \
    '{uid: $name, name: $name, type: "telegram", settings: {bottoken: $token, chatid: $chat, parse_mode: "HTML"}}')"
  if api GET "/api/v1/provisioning/contact-points?name=$CONTACT_POINT" | jq -e 'length > 0' >/dev/null; then
    api PUT "/api/v1/provisioning/contact-points/$CONTACT_POINT" "$contact" >/dev/null
  else
    api POST /api/v1/provisioning/contact-points "$contact" >/dev/null
  fi
  echo "Contact point $CONTACT_POINT sends to Telegram chat $TELEGRAM_CHAT_ID"
fi

for file in "$here"/dashboards/*.json; do
  api POST /api/dashboards/db "$(jq --arg folder "$FOLDER_UID" \
    '{dashboard: (. + {id: null}), folderUid: $folder, overwrite: true, message: "push.sh"}' "$file")" |
    jq -r '"Dashboard: \(.url)"' | sed "s|^Dashboard: |Dashboard: ${GRAFANA_URL%/}|"
done

for file in "$here"/alerts/*.json; do
  group="$(jq -r .title "$file")"
  body="$(jq --arg folder "$FOLDER_UID" --arg group "$group" --arg prometheus "$PROMETHEUS_DATASOURCE_UID" \
    --arg contact "$CONTACT_POINT" '
    .folderUid = $folder
    | .rules |= map(
        .folderUID = $folder
        | .ruleGroup = $group
        | .data |= map(if .datasourceUid == "$PROMETHEUS_UID" then .datasourceUid = $prometheus else . end)
        | .notification_settings.receiver = $contact)' "$file")"
  api PUT "/api/v1/provisioning/folder/$FOLDER_UID/rule-groups/$group" "$body" >/dev/null
  echo "Alert rules: $group ($(jq '.rules | length' <<<"$body") rules)"
done
