#!/bin/sh
set -e

settings=/usr/share/nginx/html/appsettings.json
headers=/etc/nginx/snippets/security-headers.conf
if [ ! -f /usr/share/nginx/html/index.html ] || [ ! -f "$settings" ]; then
  echo "Frontend build is incomplete: index.html and appsettings.json are required." >&2
  exit 1
fi

if [ -n "$PLAYDEX_API_URL" ]; then
  sed -i "s|__PLAYDEX_API_URL__|${PLAYDEX_API_URL}|g" "$settings"

  # An API on another origin has to be in the CSP's connect-src, or the browser blocks every call to it.
  # A same-origin URL (/api/) is already covered by 'self'. The check keeps a container restart from
  # adding the origin twice.
  api_origin=$(printf '%s\n' "$PLAYDEX_API_URL" | sed -nE 's#^(https?://[A-Za-z0-9.:-]+).*$#\1#p' | head -n 1)
  if [ -n "$api_origin" ] && ! grep -qF "connect-src 'self' ${api_origin} " "$headers"; then
    sed -i "s#connect-src 'self'#connect-src 'self' ${api_origin}#" "$headers"
  fi
fi

exec nginx -g "daemon off;"
