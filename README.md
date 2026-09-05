# ReceiptCollector
![](https://github.com/drypa/ReceiptCollector/workflows/Docker%20Image%20CI/badge.svg)
[![Go Report Card](https://goreportcard.com/badge/github.com/drypa/ReceiptCollector)](https://goreportcard.com/report/github.com/drypa/ReceiptCollector)

Russian Tax service provides mobile application "Проверка чека" to get receipt information online.
ReceiptCollector uses nalog.ru api to collect purchase data.


### how to build
```bash
sudo chmod +x ./build.sh 
./build.sh
```

### how to run
```bash
sudo chmod +x ./up.sh 
./up.sh
```

### how to stop
```bash
sudo chmod +x ./down.sh 
./down.sh
```

### Nginx Proxy Configuration
The system uses nginx as a reverse proxy for:
- Serving frontend static assets  
- Proxying API requests to Analytics service
- Terminating TLS connections

Nginx is configured with proper SSL certificates and security headers.

The host ports published by the nginx container are configurable via environment variables in `.env` (template in `.env.example`):

| Variable | Default | Description |
|----------|---------|-------------|
| `NGINX_HTTP_PORT` | `80` | Host port mapped to the container's HTTP port (80) |
| `NGINX_HTTPS_PORT` | `443` | Host port mapped to the container's HTTPS port (443) |

### Development Environment Setup
For development, all services are proxied through Nginx. The analytics service will be available at:

- API: http://localhost/api/
- Frontend: http://localhost/

To run with the new Nginx proxy:
```bash
./up.dev.sh
```

### Запуск debug-окружения одной командой

`./dev-run.sh` поднимает всё debug-окружение одной командой: dev-контейнеры (mongo/pg/nginx), TLS-сертификаты, backend (Go), миграции и API Analytics (.NET), frontend (Vite), Telegram-бот — и показывает живые логи (`tail -f`). Ctrl+C корректно останавливает все запущенные процессы.

Перед первым запуском:

```bash
cp .env.example .env
# заполните обязательные переменные: CLIENT_SECRET, BOT_TOKEN
./dev-run.sh
```

- Повторный запуск `./dev-run.sh` останавливает и перезапускает сервисы разработки (backend, Analytics API, frontend, бот) с актуальным кодом; docker-контейнеры (mongo/pg/nginx) при этом не перезапускаются и продолжают работать.
- При первом запуске может потребоваться пароль sudo: скрипт создаёт системные каталоги `/usr/share/receipts/ssl/certs`, `${TEMPLATES_PATH}`, `/var/lib/receipts/raw`, `/var/lib/receipts/error`.
- Логи сервисов: `logs/<сервис>.log`, PID-файлы: `logs/<сервис>.pid`.
- Все переменные окружения берутся из `.env` в корне проекта (шаблон — `.env.example`).

### Analytics Service (.NET 10)
The analytics service has been migrated to .NET 10. To run it locally:

```bash
# Migrate database first
cd ReceiptCollector.Analytics.Migrations && dotnet run

# Then run the API
cd ReceiptCollector.Analytics.Api && dotnet run
```

### Analytics Frontend (React + Vite)
The analytics frontend is a React SPA built with Vite. To run it locally in debug mode with HMR:

```bash
cd Analytics/frontend

# Install dependencies (first time only)
npm install

# Start the dev server with HMR
npm run dev
```

The dev server starts at `http://localhost:5173` and proxies `/api` requests to the Analytics API on port `5039`, so the Analytics API (`ReceiptCollector.Analytics.Api`) must be running first.

Other useful commands:

```bash
npm run build   # build for production (outputs to ../src/ReceiptCollector.Analytics.Api/wwwroot)
npm run lint    # run ESLint
```

### Backend (Go)
To run the backend collector locally in debug mode (requires MongoDB, e.g. started via `./up.dev.sh` or `docker-compose.develop.yml`):

```bash
cd backend

# Generate TLS certs if not already present
cd .. && ./generate-ssl-cert.sh && cd backend

# Set environment variables
export MONGO_URL=mongodb://localhost:27017
export MONGO_LOGIN=admin
export MONGO_SECRET=secret
export CLIENT_SECRET=your_client_secret
export NALOGRU_BASE_ADDR=https://irkkt-mobile.nalog.ru:8888
export TEMPLATES_PATH=/usr/share/receipts/templates
export GET_RECEIPT_WORKER_INTERVAL=1m

# Run with hot reload (optional: air) or plain:
go run .
```

- HTTP API listens on `:8888`, gRPC on `:15000` and `:15001`.
- TLS certificates are read from `/usr/share/receipts/ssl/certs/`, so generate them first with `./generate-ssl-cert.sh`.
- For hot-reload debugging install [air](https://github.com/air-verse/air) and run `air` instead of `go run .`.

### Telegram Bot (Go)
To run the Telegram bot locally in debug mode (requires the Backend gRPC to be running):

```bash
cd bot

# Set environment variables
export BOT_TOKEN=your_telegram_bot_token
export BOT_DEBUG=true
export HTTP_PROXY= # optional, leave empty
export ANALYTICS_URL=http://localhost:5039
export BACKEND_GRPC_ADDR=localhost:15000
export REPORTS_GRPC_ADDR=localhost:15001

go run .
```

- The bot connects to the backend via TLS gRPC (`BACKEND_GRPC_ADDR`, `REPORTS_GRPC_ADDR`), so the backend must be running and the same TLS certs must be present at `/usr/share/receipts/ssl/certs/certificate.crt`.
- `BOT_DEBUG=true` enables the Telegram API debug logging.

### Database

- **MongoDB 8.2.3** (`mongo:8.2.3` in docker-compose) stores raw receipts, devices and sessions.
- Backup: `./backup.sh` dumps both business databases (`receipt_collection`, `receipt-data`) into `${MONGO_BACKUP}`.
- Restore: `./restore.sh <dump-directory>`.
- Upgrading from MongoDB 4.x requires `mongodump → mongorestore` into a fresh empty data directory (WiredTiger formats are not compatible across major versions). See [ADR-015](docs/adr/015-mongodb-upgrade-8.2.md) and the ops runbook [docs/runbooks/mongodb-upgrade-manual-ops.md](docs/runbooks/mongodb-upgrade-manual-ops.md).

### Useful scripts

```javascript
//reset status to allow workers reprocess it.
db.getCollection('receipt_requests').updateMany({check_request_status: 'requested'}, {$set: {check_request_status: 'undefined'}})
//or
db.getCollection('receipt_requests').updateMany({check_request_status: 'error'}, {$set: {check_request_status: 'undefined'}})

```

```javascript
//remove obsolete fields.
db.getCollection('receipt_requests').updateMany({}, {$unset: {odfs_request_status: '', odfs_requested: ''}})
```

```javascript
//refresh session manually
db.getCollection('devices').updateOne({"_id": ObjectId("000000000000000000000000")}, {
    "$set": {
        "session_id": "XXX:XXX",
        "refresh_token": "XXX"
    }
})
```

```javascript
//reset receipts error status
db.receipt_requests.updateMany({
    "query_string": /t=2024/,
    "check_request_status": "error"
}, {$set: {"check_request_status": null}}, {})
```

### SSL Certificate Generation

To generate SSL certificates for development:

```bash
chmod +x ./generate-ssl-cert.sh
./generate-ssl-cert.sh
```
