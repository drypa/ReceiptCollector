# ---------- Стадия сборки фронтенда ----------
FROM node:22-alpine AS frontend
WORKDIR /src/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build
# Результат (vite outDir из vite.config.ts): /src/src/ReceiptCollector.Analytics.Api/wwwroot

# ---------- Стадия сборки .NET ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY *.sln .
COPY src/ReceiptCollector.Analytics.Api/ReceiptCollector.Analytics.Api.csproj ./src/ReceiptCollector.Analytics.Api/
COPY src/ReceiptCollector.Analytics.Application/ReceiptCollector.Analytics.Application.csproj ./src/ReceiptCollector.Analytics.Application/
COPY src/ReceiptCollector.Analytics.Domain/ReceiptCollector.Analytics.Domain.csproj ./src/ReceiptCollector.Analytics.Domain/
COPY src/ReceiptCollector.Analytics.Infrastructure/ReceiptCollector.Analytics.Infrastructure.csproj ./src/ReceiptCollector.Analytics.Infrastructure/
COPY src/ReceiptCollector.Analytics.Migrations/ReceiptCollector.Analytics.Migrations.csproj ./src/ReceiptCollector.Analytics.Migrations/
COPY tests/ReceiptCollector.Analytics.Api.Tests/ReceiptCollector.Analytics.Api.Tests.csproj ./tests/ReceiptCollector.Analytics.Api.Tests/
RUN dotnet restore

COPY . .
# Свежая сборка фронтенда поверх контекста (не перезаписывается `COPY . .`)
COPY --from=frontend /src/src/ReceiptCollector.Analytics.Api/wwwroot ./src/ReceiptCollector.Analytics.Api/wwwroot

WORKDIR /src/src/ReceiptCollector.Analytics.Api/
RUN dotnet publish -c Release -o /app/publish/api --no-restore

WORKDIR /src/src/ReceiptCollector.Analytics.Migrations/
RUN dotnet publish -c Release -o /app/publish/migrations --no-restore

# ---------- Runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
COPY docker-entrypoint.sh .
RUN chmod +x docker-entrypoint.sh
EXPOSE 5039
ENTRYPOINT ["/app/docker-entrypoint.sh"]
