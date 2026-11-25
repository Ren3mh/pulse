# Builds Pulse.Api. Build context is the repo root (see compose.yaml).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Pulse.Api/Pulse.Api.csproj src/Pulse.Api/
RUN dotnet restore src/Pulse.Api/Pulse.Api.csproj
COPY src/Pulse.Api/ src/Pulse.Api/
RUN dotnet publish src/Pulse.Api/Pulse.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Pulse.Api.dll"]
