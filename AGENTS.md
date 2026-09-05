# ReceiptCollector Agent Instructions

## Architecture Overview

This is a **three-service microservice system**:

1. **Backend (Go)**: `backend/` - Collects receipts from nalog.ru API, storage in MongoDB
2. **Telegram Bot (Go)**: `bot/` - Telegram interface that connects to Backend via gRPC
3. **Analytics (.NET 8)**: `Analytics/` - Migrates receipts from MongoDB → PostgreSQL, provides analytics UI

## Commands

### Build and Run (Production/Docker)

```bash
./build.sh    # Builds all Docker images
./up.sh       # Starts all services (pulls, start container network)
./down.sh     # Stops all services
./up.dev.sh   # Development: starts services with exposed ports for local access
```

### Build (Go Backend Only - CI-style)

```bash
cd backend
CGO_ENABLED=0 go build -o receipt_collector .
```

### Run Analytics (.NET 8 Microservice)

```bash
# Migrate first
cd ../ReceiptCollector.Analytics.Migrations && dotnet run

# Then API
cd ../ReceiptCollector.Analytics.Api && dotnet run
```

## Services Flow

- Backend: HTTP + gRPC endpoints on ports 8888, 15000 (gRPC), 15001 (reports gRPC)
- Bot connects to Backend via `BACKEND_GRPC_ADDR=collector:15000`
- Analytics connects to MongoDB for migration, PostgreSQL for normalized data

## Testing

Tests exist in `_test.go` files. Run with:

```bash
cd backend && go test ./...
cd bot && go test ./...
cd Analytics && dotnet test
```

### Worker Tests

Workers run via goroutines in `main.go`:
- `worker.GetReceiptStart(ctx, settings)` - polls nalog.ru API
- `worker.GetElectronicReceiptStart(ctx)` - daily job at 01:00
- Workers are **long-running background tasks**; don't forget to call `cancel()` on signal

### Test Fixtures & Gotchas

- Worker tests may fail if MongoDB isn't running with right credentials
- Some fixtures assume database is pre-populated
- Integration tests likely need real nalog.ru access or mocking

## Configuration

Environment variables required (see `.env`):

| Variable | Default/Example | Source |
|----------|-----------------|--------|
| MONGO_URL | `mongodb://mongo:27017` | DockerCompose |
| MONGO_LOGIN | `admin` | From `.env` |
| MONGO_SECRET | `secret` | From `.env` |
| CLIENT_SECRET | — | Required |
| NALOGRU_BASE_ADDR | `https://irkkt-mobile.nalog.ru:8888` | env var |
| TEMPLATES_PATH | `/usr/share/receipts/templates` | file mount |
| SSL_CERTS_PATH | `/usr/share/receipts/ssl/certs/` | file mount |
| RAW_TICKET_DUMP_PATH | `/var/lib/receipts/raw/` | file mount |
| GET_TICKET_ERROR_PATH | `/var/lib/receipts/error/` | file mount |
| NGINX_HTTP_PORT | `80` | From `.env` (host map to nginx :80) |
| NGINX_HTTPS_PORT | `443` | From `.env` (host map to nginx :443) |

Analytics connection strings are in `appsettings.{*.Development}.json`.

## Key Code Locations

### Backend (`backend/`)

- `main.go`: HTTP server setup, goroutine workers, gRPC listeners
- `workers/get.go`: main polling worker logic (nalog.ru API calls)
- `workers/get_test.go`: test coverage for worker timing/retry
- `receipts/repository.go` → `processor.go`: core processing
- `users/`: registration, Telegram-linked accounts (`/api/login`, `/internal/account`)
- `dispose/dispose.go`: cleanup handlers (disconnect mongo, etc.)

### Bot (`bot/`)

- `main.go`: gRPC client to backend, command registrar
- `commands/`: registered commands (`start`, `register`, `confirmation`, `report`)
- Connects via gRPC to Backend service at `backendGrpcAddress`

## Common Mistakes to Avoid

1. **Workers are async**: Don't assume they finish; signals cancel context but graceful shutdown is needed
2. **TLS certs location**: Both services read from `/usr/share/receipts/ssl/certs/` — missing certs = startup failure
3. **MongoDB must start before collector**: `depends_on: - mongo` in docker-compose
4. **Analytics order matters**: Run migrations (`dotnet run` in Migrations project) before API
5. **Go build context**: Dockerfile copies `backend/` dir to image; full repo is not needed
6. **Template path**: Must pre-populate `/usr/share/receipts/templates/` with receipt templates

## CI Workflows

- `.github/workflows/build-backend-image.yml`: Builds Go backend and bot images on push/PullRequest
- `.github/workflows/codeql-analysis.yml`: Scans for security issues (Go, JS) every push or weekly schedule

## Database Notes

- **MongoDB 8.2.3** (`mongo:8.2.3`): Raw receipt data, device tracking, sessions.
  Volume: `${MONGO_DATA}` — должен указывать на **чистый каталог**, инициализированный
  версией 8.x (формат данных WiredTiger несовместим с 4.x). Обновление с 4.1
  выполнялось по схеме `mongodump → mongorestore`: см.
  [ADR-015](docs/adr/015-mongodb-upgrade-8.2.md) и инструкцию для персонала
  [docs/runbooks/mongodb-upgrade-manual-ops.md](docs/runbooks/mongodb-upgrade-manual-ops.md).
- **Бэкап**: `./backup.sh` дампит **обе** бизнес-базы (`receipt_collection` и
  `receipt-data`) в `${MONGO_BACKUP}`; восстановление — `./restore.sh <каталог-дампа>`.
- **PostgreSQL**: Analytics service only. Migrations run via separate `.Net` project before API starts.

## Development Shortcuts

```bash
# Start everything with ports exposed
./up.dev.sh && ./build-frontend.sh  # exposes nginx, backend, analytics ports

# Reset worker status for debugging
mongosh receipt-data --eval "db.getCollection('receipt_requests').updateMany({check_request_status: 'requested'}, {\$set: {check_request_status: 'undefined'}})"
```

## Framework Quirks

- **Go**: Standard library signals handle shutdown. Workers use `gocron` for scheduling.
- **.NET**: EF Core migrations are separate project (`ReceiptCollector.Analytics.Migrations`)
- **Nginx**: Built from `docker/nginx`; serves all frontend traffic, reverse-proxies API
