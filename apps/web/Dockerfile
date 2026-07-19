FROM oven/bun:1 AS css

WORKDIR /src
COPY package.json bun.lock ./
RUN bun install --frozen-lockfile
COPY . .
RUN bun run css:build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src
COPY . .
COPY --from=css /src/wwwroot/css/app.css ./wwwroot/css/app.css
RUN dotnet publish Playdex.Web.csproj -c Release -o /app/publish

FROM nginx:1.27-alpine

COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
COPY docker-entrypoint.sh /docker-entrypoint.sh
RUN chmod +x /docker-entrypoint.sh

EXPOSE 5056
HEALTHCHECK --interval=30s --timeout=3s --retries=3 \
  CMD wget -q -O /dev/null http://127.0.0.1:5056/ || exit 1

ENTRYPOINT ["/docker-entrypoint.sh"]
