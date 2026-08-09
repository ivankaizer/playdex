#!/bin/sh
set -e

framework_dir=/usr/share/nginx/html/_framework
if [ ! -f "$framework_dir/dotnet.js" ] ||
   ! find "$framework_dir" -type f -name '*.wasm' -print -quit | grep -q .; then
  echo "Blazor framework publish is incomplete: dotnet.js and WASM assets are required." >&2
  exit 1
fi

if [ -n "$PLAYDEX_API_URL" ]; then
  sed -i "s|__PLAYDEX_API_URL__|${PLAYDEX_API_URL}|g" /usr/share/nginx/html/appsettings.json
fi

exec nginx -g "daemon off;"
