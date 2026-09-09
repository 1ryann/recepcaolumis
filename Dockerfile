# syntax=docker/dockerfile:1

# ---- Node 22 (build-time only) --------------------------------------------------
FROM node:22-bookworm-slim AS node

# ---- Build: .NET SDK 10 + Node 22 --------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ENV DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
# Bring Node 22 + npm into the SDK image (no NodeSource).
COPY --from=node /usr/local/bin/ /usr/local/bin/
COPY --from=node /usr/local/lib/node_modules/ /usr/local/lib/node_modules/
RUN node --version && npm --version
WORKDIR /src
# Restore first for layer caching.
COPY recepcaototem.sln ./
COPY recepcaototem/recepcaototem.csproj recepcaototem/
COPY src/ src/
COPY tools/ tools/
COPY tests/ tests/
RUN dotnet restore recepcaototem/recepcaototem.csproj
# Bring in the rest (ClientApp sources etc.).
COPY . .
# Publish. The csproj BuildClientApp target runs `npm ci && npm run build` in ClientApp/
# and copies ClientApp/dist/** (minus .map) into the publish output's wwwroot/.
RUN dotnet publish recepcaototem/recepcaototem.csproj -c Release -o /app/publish /p:UseAppHost=false
# Production bundle safety gate: fail the build if a dev/mock artifact leaked into dist.
RUN cd recepcaototem/ClientApp && npm run verify:production-bundle
# Prove the SPA is in the publish output.
RUN test -f /app/publish/wwwroot/index.html && ls /app/publish/wwwroot/assets/*.js >/dev/null

# ---- Runtime: ASP.NET 10 (Debian bookworm) --------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
ENV DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
WORKDIR /app
COPY --from=build /app/publish ./
# /data is mounted as a volume at runtime; ensure the two subdirs exist before the app
# validates them (Production refuses a missing Storage__PrivateFilesPath).
ENTRYPOINT ["/bin/sh", "-c", "mkdir -p /data/dpkeys /data/private && exec dotnet recepcaototem.dll"]
