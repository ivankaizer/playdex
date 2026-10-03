#!/bin/sh
set -e

settings=/usr/share/nginx/html/appsettings.json
if [ ! -f /usr/share/nginx/html/index.html ] || [ ! -f "$settings" ]; then
  echo "Frontend build is incomplete: index.html and appsettings.json are required." >&2
  exit 1
fi

if [ -n "$PLAYDEX_API_URL" ]; then
  sed -i "s|__PLAYDEX_API_URL__|${PLAYDEX_API_URL}|g" "$settings"
fi

exec nginx -g "daemon off;"
