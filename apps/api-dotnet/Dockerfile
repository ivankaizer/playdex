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
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "Playdex.Api.dll"]
