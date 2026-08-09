FROM oven/bun:1 AS css

WORKDIR /src/Web
COPY src/Web/package.json src/Web/bun.lock ./
RUN bun install --frozen-lockfile
COPY src/Web/ .
RUN bun run css:build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/Web/Web.csproj Web/
RUN dotnet restore Web/Web.csproj
COPY src/Web/ Web/
COPY --from=css /src/Web/wwwroot/css/app.css Web/wwwroot/css/app.css
RUN dotnet publish Web/Web.csproj -c Release -o /app/publish
RUN test -f /app/publish/wwwroot/_framework/dotnet.js \
    && find /app/publish/wwwroot/_framework -type f -name '*.wasm' -print -quit | grep -q .

FROM nginx:1.27-alpine

COPY src/Web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
COPY src/Web/docker-entrypoint.sh /docker-entrypoint.sh
RUN chmod +x /docker-entrypoint.sh

EXPOSE 5056
HEALTHCHECK --interval=30s --timeout=3s --retries=3 \
  CMD wget -q -O /dev/null http://127.0.0.1:5056/ || exit 1

ENTRYPOINT ["/docker-entrypoint.sh"]
