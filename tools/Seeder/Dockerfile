FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/Api/Api.csproj src/Api/
COPY src/Persistence/Persistence.csproj src/Persistence/
COPY src/ServiceDefaults/ src/ServiceDefaults/
COPY tools/Seeder/Seeder.csproj tools/Seeder/
RUN dotnet restore tools/Seeder/Seeder.csproj
COPY src/Api/ src/Api/
COPY src/Persistence/ src/Persistence/
COPY tools/Seeder/ tools/Seeder/
RUN dotnet publish tools/Seeder/Seeder.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Seeder.dll"]
