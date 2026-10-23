FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/Api/Api.csproj Api/
COPY src/Persistence/Persistence.csproj Persistence/
COPY src/ServiceDefaults/ ServiceDefaults/
RUN dotnet restore Api/Api.csproj
COPY src/Api/ Api/
COPY src/Persistence/ Persistence/
RUN dotnet publish Api/Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# The aspnet image listens on 8080 by default; EXPOSE and the health check below use 3000.
ENV ASPNETCORE_HTTP_PORTS=3000
EXPOSE 3000
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl libgssapi-krb5-2 libfontconfig1 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
USER $APP_UID
HEALTHCHECK --interval=30s --timeout=3s --retries=3 \
  CMD curl -fsS http://127.0.0.1:3000/health/live || exit 1
ENTRYPOINT ["dotnet", "Api.dll"]
