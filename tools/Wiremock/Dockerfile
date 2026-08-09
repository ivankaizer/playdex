FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY tools/Wiremock/Wiremock.csproj tools/Wiremock/
RUN dotnet restore tools/Wiremock/Wiremock.csproj
COPY tools/Wiremock/ tools/Wiremock/
RUN dotnet publish tools/Wiremock/Wiremock.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8888
ENTRYPOINT ["dotnet", "Wiremock.dll"]
