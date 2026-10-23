FROM oven/bun:1 AS build

WORKDIR /src/Frontend
COPY src/Frontend/package.json src/Frontend/bun.lock ./
RUN bun install --frozen-lockfile
COPY src/Frontend/ .
RUN bun run build \
    && test -f dist/index.html \
    && test -f dist/appsettings.json \
    && bun run check:csp

FROM nginx:1.29-alpine

COPY src/Frontend/nginx.conf /etc/nginx/conf.d/default.conf
COPY src/Frontend/security-headers.conf /etc/nginx/snippets/security-headers.conf
COPY --from=build /src/Frontend/dist /usr/share/nginx/html
COPY src/Frontend/docker-entrypoint.sh /docker-entrypoint.sh
RUN chmod +x /docker-entrypoint.sh

EXPOSE 5056
HEALTHCHECK --interval=30s --timeout=3s --retries=3 \
  CMD wget -q -O /dev/null http://127.0.0.1:5056/ || exit 1

ENTRYPOINT ["/docker-entrypoint.sh"]
