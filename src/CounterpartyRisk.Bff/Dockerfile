# syntax=docker/dockerfile:1
# Demo image: Angular front end served by the BFF, dashboard endpoints on synthetic data.
# Build context: repository root.  docker compose up --build  (see compose.yaml)

# 1. Angular production build
FROM node:26-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npx ng build --configuration production

# 2. BFF publish
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS bff
WORKDIR /src
COPY global.json nuget.config .editorconfig Directory.Build.props Directory.Packages.props ./
COPY src/CounterpartyRisk.ServiceDefaults/CounterpartyRisk.ServiceDefaults.csproj src/CounterpartyRisk.ServiceDefaults/packages.lock.json src/CounterpartyRisk.ServiceDefaults/
COPY src/CounterpartyRisk.Bff/CounterpartyRisk.Bff.csproj src/CounterpartyRisk.Bff/packages.lock.json src/CounterpartyRisk.Bff/
RUN dotnet restore src/CounterpartyRisk.Bff/CounterpartyRisk.Bff.csproj --locked-mode
COPY src/CounterpartyRisk.ServiceDefaults/ src/CounterpartyRisk.ServiceDefaults/
COPY src/CounterpartyRisk.Bff/ src/CounterpartyRisk.Bff/
RUN dotnet publish src/CounterpartyRisk.Bff/CounterpartyRisk.Bff.csproj -c Release --no-restore -o /app

# 3. Runtime: chiseled image, no shell, non-root user
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled
WORKDIR /app
COPY --from=bff /app ./
COPY --from=web /web/dist/counterparty-risk-web/browser ./wwwroot
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "CounterpartyRisk.Bff.dll"]
