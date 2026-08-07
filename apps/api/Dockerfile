FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Playdex.Api/Playdex.Api.csproj Playdex.Api/
RUN dotnet restore Playdex.Api/Playdex.Api.csproj
COPY . .
WORKDIR /src/Playdex.Api
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
EXPOSE 3000
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl libgssapi-krb5-2 libfontconfig1 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
USER $APP_UID
HEALTHCHECK --interval=30s --timeout=3s --retries=3 \
  CMD curl -fsS http://127.0.0.1:3000/health/live || exit 1
ENTRYPOINT ["dotnet", "Playdex.Api.dll"]
