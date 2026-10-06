# LanWatch: lancache + AdGuard monitor. Multi-stage build: SDK → slim ASP.NET runtime, non-root.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the layer caches across source edits.
COPY Directory.Build.props Directory.Packages.props LanWatch.slnx ./
COPY src/LanWatch.Shared/LanWatch.Shared.csproj src/LanWatch.Shared/
COPY src/LanWatch.Client/LanWatch.Client.csproj src/LanWatch.Client/
COPY src/LanWatch.Server/LanWatch.Server.csproj src/LanWatch.Server/
RUN dotnet restore src/LanWatch.Server/LanWatch.Server.csproj

# Copy the sources plus .git (if present) so the build knows which commit it is.
COPY . .
RUN dotnet publish src/LanWatch.Server/LanWatch.Server.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# /logs: lancache logs (mount read-only). /data: SQLite + cached game art. /cache: optional, for disk usage.
ENV ASPNETCORE_URLS=http://+:8080 \
    LOGS_PATH=/logs \
    DATA_PATH=/data \
    DOTNET_gcServer=0
RUN mkdir -p /data /logs && chown -R app:app /data
USER app
EXPOSE 8080
VOLUME ["/data"]

# No HEALTHCHECK: the runtime image has no curl. Probe GET /healthz from outside if needed.

ENTRYPOINT ["dotnet", "LanWatch.Server.dll"]
