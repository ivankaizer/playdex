#!/bin/sh
set -e

if [ -n "$PLAYDEX_API_URL" ]; then
  sed -i "s|__PLAYDEX_API_URL__|${PLAYDEX_API_URL}|g" /usr/share/nginx/html/appsettings.json
fi

exec nginx -g "daemon off;"
