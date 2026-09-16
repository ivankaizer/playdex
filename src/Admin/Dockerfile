FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/Admin/Admin.csproj
RUN dotnet publish src/Admin/Admin.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:3100
EXPOSE 3100
ENTRYPOINT ["dotnet", "Admin.dll"]
