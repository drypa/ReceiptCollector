#!/usr/bin/env bash
#
# dev-run.sh — запуск всех сервисов ReceiptCollector в режиме отладки одной командой.
# Порядок: инфраструктура (docker) -> TLS-сертификаты -> [stop-and-restart] ->
#          backend -> миграции -> Analytics API -> frontend -> Telegram bot.
# Все переменные окружения берутся из .env (корень проекта).
# Логи и PID-файлы пишутся в logs/. Ctrl+C останавливает все процессы.
# Повторный запуск перезапускает не-инфраструктурные сервисы (FR-9), а
# docker-контейнеры (mongo/pg/nginx) не трогает.

set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$PROJECT_ROOT"

LOG_DIR="$PROJECT_ROOT/logs"

# --- Константы ---------------------------------------------------------------

# Не-инфраструктурные сервисы: имя, каталог, порты (для детекта/стопа).
# Инфраструктурные порты (27017/5432/8080) НИКОГДА не попадают в этот список.
# shellcheck disable=SC2034 # используется как документация маппинга сервисов
declare -A SERVICE_PORTS=(
    [backend]="8888 15000 15001"
    [analytics-api]="5039"
    [frontend]="5173"
)
# shellcheck disable=SC2034 # используется как документация маппинга сервисов
SERVICE_DIRS=(backend:"$PROJECT_ROOT/backend" analytics-api:"$PROJECT_ROOT/Analytics/src/ReceiptCollector.Analytics.Api" frontend:"$PROJECT_ROOT/Analytics/frontend" bot:"$PROJECT_ROOT/bot")
# bot портов не слушает — детектится по PID-файлу и pgrep+cwd.

# --- Вспомогательные функции -------------------------------------------------

log() { printf '[dev-run] %s\n' "$*"; }
err() { printf '[dev-run][ОШИБКА] %s\n' "$*" >&2; }

require_cmd() {
    local cmd
    for cmd in "$@"; do
        if ! command -v "$cmd" >/dev/null 2>&1; then
            err "Не найдена зависимость: $cmd. Установите её и повторите."
            exit 1
        fi
    done
}

# is_port_open <host> <port> — проверяет, слушается ли TCP-порт
is_port_open() {
    (exec 3<>"/dev/tcp/$1/$2") 2>/dev/null
}

# wait_for_port <host> <port> <имя> [таймаут_сек] — ждёт готовности порта
wait_for_port() {
    local host="$1" port="$2" name="$3" timeout="${4:-60}"
    local deadline
    deadline=$(( $(date +%s) + timeout ))
    log "Ожидаю готовности $name ($host:$port), таймаут ${timeout}с..."
    while [ "$(date +%s)" -lt "$deadline" ]; do
        if is_port_open "$host" "$port"; then
            log "$name готов ($host:$port)."
            return 0
        fi
        sleep 1
    done
    err "$name не стал доступен на $host:$port за ${timeout}с."
    return 1
}

# wait_port_free <порт> <имя> — ждёт освобождения порта после остановки
wait_port_free() {
    local port="$1" name="$2" timeout=15
    local deadline
    deadline=$(( $(date +%s) + timeout ))
    while [ "$(date +%s)" -lt "$deadline" ]; do
        if ! is_port_open 127.0.0.1 "$port"; then
            log "Порт $port освобождён ($name)."
            return 0
        fi
        sleep 0.5
    done
    err "Порт $port не освободился за ${timeout}с после остановки $name."
    return 1
}

# port_pids <порт> — PID'ы процессов, слушающих TCP-порт.
# Приоритет: ss (iproute2) -> lsof -> fuser. Без жёсткой зависимости.
# ВАЖНО: все ветви завершаются успешно (|| true), т.к. под `set -o pipefail`
# пустой результат grep даёт ненулевой код и `set -e` оборвал бы скрипт.
port_pids() {
    local port="$1"
    if command -v ss >/dev/null 2>&1; then
        ss -tlnp 2>/dev/null | grep -E ":$port\b" | grep -oE 'pid=[0-9]+' | cut -d= -f2 | sort -u || true
    elif command -v lsof >/dev/null 2>&1; then
        lsof -ti tcp:"$port" 2>/dev/null | sort -u || true
    elif command -v fuser >/dev/null 2>&1; then
        fuser "$port"/tcp 2>/dev/null | tr -s ' ' '\n' | sed '/^$/d' || true
    fi
}

# kill_tree <pid> [сигнал] — рекурсивно убивает процесс и всех потомков.
# Нужен для чужих экземпляров: они в группе терминала разработчика,
# групповой kill -- -PID мог бы убить терминал (ADR-013, D8).
kill_tree() {
    local pid="$1" sig="${2:-TERM}"
    local child
    for child in $(pgrep -P "$pid" 2>/dev/null || true); do
        kill_tree "$child" "$sig"
    done
    kill -s "$sig" "$pid" 2>/dev/null || true
}

# launch <имя> <каталог> <команда...> — фоновый запуск в отдельной группе процессов.
# Пишет PID-файл logs/<имя>.pid для детекции при повторном запуске.
PIDS=()
launch() {
    local name="$1" dir="$2"; shift 2
    ( cd "$dir" && exec setsid "$@" ) >>"$LOG_DIR/$name.log" 2>&1 &
    local pid=$!
    echo "$pid" > "$LOG_DIR/$name.pid"
    PIDS+=("$name:$pid")
    log "Запущен $name (pid $pid, группа $pid). Лог: $LOG_DIR/$name.log"
}

# stop_service <имя> <каталог> [порты...] — останавливает ранее запущенный
# экземпляр сервиса (свой через PID-файл, чужой через порт/cwd).
# Инфраструктурные порты сюда передавать ЗАПРЕЩЕНО (assert ниже).
stop_service() {
    local name="$1" dir="$2"; shift 2
    local ports=("$@")
    local pidfile="$LOG_DIR/$name.pid"

    # assert: защита инфраструктуры (FR-9.3)
    local p
    for p in "${ports[@]:-}"; do
        case "$p" in
            27017|5432|8080) err "Внутренняя ошибка: попытка остановить инфраструктурный порт $p ($name)"; return 1;;
        esac
    done

    # 1) PID-файл: свой экземпляр (группа процессов setsid)
    if [ -f "$pidfile" ]; then
        local pid
        pid=$(cat "$pidfile" 2>/dev/null || true)
        if [ -n "$pid" ] && kill -0 "$pid" 2>/dev/null; then
            local cwd
            cwd=$(readlink "/proc/$pid/cwd" 2>/dev/null || true)
            if [ "$cwd" = "$dir" ]; then
                log "  $name: останавливаю свой экземпляр (pid $pid, группа процессов)"
                kill -TERM -- "-$pid" 2>/dev/null || kill -TERM "$pid" 2>/dev/null || true
            else
                log "  $name: PID-файл устарел (cwd $cwd != $dir), игнорирую"
            fi
        else
            log "  $name: PID-файл устарел (процесс не найден)"
        fi
        rm -f "$pidfile"
    fi

    # 2) Порт-детект: чужой экземпляр (запущен вручную) — рекурсивное убийство дерева
    for p in "${ports[@]:-}"; do
        [ -n "$p" ] || continue
        local found
        found=$(port_pids "$p" || true)
        if [ -n "$found" ]; then
            log "  $name: порт $p занят чужим процессом ($(echo "$found" | tr '\n' ' ')) — убиваю дерево"
            local pid2
            for pid2 in $found; do
                kill_tree "$pid2" TERM
            done
        fi
    done

    # 3) Бот (без портов): pgrep по cmdline с фильтром по рабочему каталогу
    if [ "${#ports[@]:-0}" -eq 0 ]; then
        local bpid
        for bpid in $(pgrep -f 'go run' 2>/dev/null || true) $(pgrep -x bot 2>/dev/null || true); do
            local bcwd
            bcwd=$(readlink "/proc/$bpid/cwd" 2>/dev/null || true)
            if [ "$bcwd" = "$dir" ]; then
                log "  $name: найден чужой процесс $bpid (cwd=$dir) — убиваю дерево"
                kill_tree "$bpid" TERM
            fi
        done
    fi

    # 4) Ожидание фактического завершения (grace 10с, затем KILL)
    local i=0
    while [ "$i" -lt 20 ]; do
        local alive=0
        if [ -f "$pidfile" ] && kill -0 "$(cat "$pidfile" 2>/dev/null || echo 0)" 2>/dev/null; then
            alive=1
        else
            for p in "${ports[@]:-}"; do
                if [ -n "$p" ] && is_port_open 127.0.0.1 "$p"; then
                    alive=1
                fi
            done
        fi
        [ "$alive" -eq 0 ] && break
        sleep 0.5; i=$((i+1))
    done
    for p in "${ports[@]:-}"; do
        [ -n "$p" ] && wait_port_free "$p" "$name"
    done
}

# cleanup — останавливает процессы, запущенные ЭТИМ экземпляром (FR-8.3)
cleanup() {
    local rc=$?
    log "Останавливаю сервисы, запущенные этим экземпляром..."
    for entry in "${PIDS[@]:-}"; do
        local name="${entry%%:*}" pid="${entry##*:}"
        [ -n "$name" ] || continue
        if kill -0 "$pid" 2>/dev/null; then
            log "  TERM -> $name (pid $pid, группа $pid)"
            kill -TERM -- "-$pid" 2>/dev/null || kill -TERM "$pid" 2>/dev/null || true
        fi
        rm -f "$LOG_DIR/$name.pid"
    done
    sleep 2
    for entry in "${PIDS[@]:-}"; do
        local pid="${entry##*:}"
        if kill -0 "$pid" 2>/dev/null; then
            log "  KILL -> pid $pid (не завершился за 2с)"
            kill -KILL -- "-$pid" 2>/dev/null || kill -KILL "$pid" 2>/dev/null || true
        fi
    done
    log "Все процессы остановлены."
    exit "$rc"
}
trap cleanup EXIT

# --- 0. Подготовка -----------------------------------------------------------

log "ReceiptCollector debug-окружение. Корень: $PROJECT_ROOT"

# Проверка .env (FR-2.2)
if [ ! -f "$PROJECT_ROOT/.env" ]; then
    err "Файл .env не найден в $PROJECT_ROOT"
    err "Создайте его из шаблона и заполните значения:"
    err "  cp .env.example .env"
    err "  # затем отредактируйте .env (обязательны CLIENT_SECRET и BOT_TOKEN)"
    exit 1
fi

# Подключение .env: все переменные экспортируются дочерним процессам
set -a
# shellcheck disable=SC1091
source "$PROJECT_ROOT/.env"
set +a

# Дефолты для локальной разработки (не перезаписывают значения из .env)
export MONGO_URL="${MONGO_URL:-mongodb://localhost:27017}"
export MONGO_LOGIN="${MONGO_LOGIN:-admin}"
export MONGO_SECRET="${MONGO_SECRET:-secret}"
export CLIENT_SECRET="${CLIENT_SECRET:-}"
export OPEN_URL="${OPEN_URL:-http://localhost:5173/login}"
export NALOGRU_BASE_ADDR="${NALOGRU_BASE_ADDR:-https://irkkt-mobile.nalog.ru:8888}"
export TEMPLATES_PATH="${TEMPLATES_PATH:-/usr/share/receipts/templates}"
export GET_RECEIPT_WORKER_INTERVAL="${GET_RECEIPT_WORKER_INTERVAL:-1m}"
export BACKEND_GRPC_HOST="${BACKEND_GRPC_HOST:-localhost}"

export BOT_TOKEN="${BOT_TOKEN:-}"
export BOT_DEBUG="${BOT_DEBUG:-true}"
export HTTP_PROXY="${HTTP_PROXY:-}"
export ANALYTICS_URL="${ANALYTICS_URL:-http://localhost:5039}"
export BACKEND_GRPC_ADDR="${BACKEND_GRPC_ADDR:-localhost:15000}"
export REPORTS_GRPC_ADDR="${REPORTS_GRPC_ADDR:-localhost:15001}"

export ANALYTICS_SYNC_SKIP="${ANALYTICS_SYNC_SKIP:-true}"
export ANALYTICS_AUTHLINK_BASE_URL="${ANALYTICS_AUTHLINK_BASE_URL:-http://localhost:8080}"
export ANALYTICS_ADMIN_TELEGRAM_ID_0="${ANALYTICS_ADMIN_TELEGRAM_ID_0:-123456789}"
export ANALYTICS_ADMIN_TELEGRAM_ID_1="${ANALYTICS_ADMIN_TELEGRAM_ID_1:-987654321}"

# Производные переменные для .NET-сервисов (префикс RECEIPTCOLLECTOR_)
# Собираются из .env, чтобы не расходиться с appsettings.Development.json.
export RECEIPTCOLLECTOR_Infrastructure__Postgres__ConnectionString="Host=localhost;Port=5432;Database=receipts;Username=${PG_LOGIN:-admin};Password=${PG_SECRET:-secret}"
export RECEIPTCOLLECTOR_Infrastructure__Receipts__Mongo__ConnectionString="mongodb://${MONGO_LOGIN}:${MONGO_SECRET}@localhost:27017/receipt_collection?authSource=admin"
export RECEIPTCOLLECTOR_Infrastructure__Receipts__Mongo__Database="receipt_collection"
export RECEIPTCOLLECTOR_Infrastructure__Receipts__Mongo__Collection="receipt_requests"
export RECEIPTCOLLECTOR_Infrastructure__Receipts__Mongo__UsersCollection="system_users"
export RECEIPTCOLLECTOR_Infrastructure__Receipts__Synchronization__Skip="${ANALYTICS_SYNC_SKIP}"
export RECEIPTCOLLECTOR_Infrastructure__AuthLinks__BaseUrl="${ANALYTICS_AUTHLINK_BASE_URL}"
export RECEIPTCOLLECTOR_Infrastructure__AdminUsers__TelegramIds__0="${ANALYTICS_ADMIN_TELEGRAM_ID_0}"
export RECEIPTCOLLECTOR_Infrastructure__AdminUsers__TelegramIds__1="${ANALYTICS_ADMIN_TELEGRAM_ID_1}"
export RECEIPTCOLLECTOR_MigrationScripts__DirectoryPath="Scripts"
export RECEIPTCOLLECTOR_MigrationScripts__CommandTimeoutSeconds="60"

mkdir -p "$LOG_DIR"

require_cmd docker go dotnet npm openssl sudo pgrep
# Для остановки чужих экземпляров нужна хотя бы одна из утилит (ADR-013, D8)
if ! command -v ss >/dev/null 2>&1 && ! command -v lsof >/dev/null 2>&1 && ! command -v fuser >/dev/null 2>&1; then
    err "Не найдена ни одна из утилит для детекта процессов по порту: ss, lsof, fuser."
    err "Установите iproute2 (ss) — она доступна практически везде."
    exit 1
fi

# --- 1. Инфраструктура (mongo/pg/nginx) -------------------------------------

log "[1/8] Поднимаю dev-контейнеры (./up.dev.sh)..."
./up.dev.sh

# --- 2. TLS-сертификаты и системные пути -------------------------------------

log "[2/8] Готовлю TLS-сертификаты и системные пути..."

SYSTEM_CERT_DIR="/usr/share/receipts/ssl/certs"
SYSTEM_TEMPLATES_DIR="${TEMPLATES_PATH}"
RAW_DIR="/var/lib/receipts/raw"
ERR_DIR="/var/lib/receipts/error"

ensure_writable_dir() {
    local d="$1"
    if [ -d "$d" ] && [ -w "$d" ]; then
        return 0
    fi
    log "  Создаю каталог $d (может потребоваться пароль sudo)..."
    if [ ! -d "$d" ]; then
        sudo mkdir -p "$d"
    fi
    if [ ! -w "$d" ]; then
        sudo chown "$(id -un)" "$d"
    fi
}

for d in "$SYSTEM_CERT_DIR" "$SYSTEM_TEMPLATES_DIR" "$RAW_DIR" "$ERR_DIR"; do
    ensure_writable_dir "$d"
done

# Генерация сертификатов ТОЛЬКО при их отсутствии (FR-3.2, NFR-2)
if [ ! -f "$PROJECT_ROOT/ssl/certificate.crt" ] || [ ! -f "$PROJECT_ROOT/ssl/private.key" ]; then
    log "  Сертификаты не найдены — запускаю ./generate-ssl-cert.sh"
    mkdir -p "$PROJECT_ROOT/ssl"
    ./generate-ssl-cert.sh
else
    log "  Сертификаты уже существуют (ssl/) — пропускаю генерацию."
fi

cp -f "$PROJECT_ROOT/ssl/certificate.crt" "$SYSTEM_CERT_DIR/"
cp -f "$PROJECT_ROOT/ssl/private.key" "$SYSTEM_CERT_DIR/"

cp -f "$PROJECT_ROOT/backend/render/templates/"*.html "$SYSTEM_TEMPLATES_DIR/" 2>/dev/null || \
    log "  Внимание: не найдены шаблоны в backend/render/templates"

# --- 3. Stop-and-restart: останавливаем уже запущенные сервисы (FR-9) --------

log "[3/8] Останавливаю ранее запущенные не-инфраструктурные сервисы (FR-9)..."

stop_service "backend"        "$PROJECT_ROOT/backend"        8888 15000 15001
stop_service "analytics-api"  "$PROJECT_ROOT/Analytics/src/ReceiptCollector.Analytics.Api"  5039
stop_service "frontend"       "$PROJECT_ROOT/Analytics/frontend"  5173
stop_service "bot"            "$PROJECT_ROOT/bot"            # портов нет

# --- 4. Backend (go run .) ----------------------------------------------------

log "[4/8] Backend..."
wait_for_port 127.0.0.1 27017 "MongoDB" 60

launch "backend" "$PROJECT_ROOT/backend" go run .
wait_for_port 127.0.0.1 15000 "Backend gRPC" 90

# --- 5. Миграции Analytics (синхронно, до API) --------------------------------

log "[5/8] Миграции Analytics (PostgreSQL)..."
wait_for_port 127.0.0.1 5432 "PostgreSQL" 60

MIGRATIONS_DIR="$PROJECT_ROOT/Analytics/src/ReceiptCollector.Analytics.Migrations"
if ( cd "$MIGRATIONS_DIR" && dotnet run ) >>"$LOG_DIR/migrations.log" 2>&1; then
    log "  Миграции применены успешно."
else
    err "Миграции завершились с ошибкой. Смотрите $LOG_DIR/migrations.log"
    exit 1
fi

# --- 6. Analytics API (dotnet run, :5039) --------------------------------------

log "[6/8] Analytics API..."
API_DIR="$PROJECT_ROOT/Analytics/src/ReceiptCollector.Analytics.Api"
launch "analytics-api" "$API_DIR" env \
    ASPNETCORE_ENVIRONMENT=Development \
    "ASPNETCORE_URLS=http://*:5039" \
    dotnet run
wait_for_port 127.0.0.1 5039 "Analytics API" 90

# --- 7. Frontend (npm run dev, :5173) ------------------------------------------

log "[7/8] Frontend (Vite)..."
FRONTEND_DIR="$PROJECT_ROOT/Analytics/frontend"
if [ ! -d "$FRONTEND_DIR/node_modules" ]; then
    log "  node_modules не найден — выполняю npm install"
    ( cd "$FRONTEND_DIR" && npm install ) >>"$LOG_DIR/frontend.log" 2>&1
fi
launch "frontend" "$FRONTEND_DIR" npm run dev
wait_for_port 127.0.0.1 5173 "Frontend" 60

# --- 8. Telegram Bot (go run .) -------------------------------------------------

log "[8/8] Telegram Bot..."
launch "bot" "$PROJECT_ROOT/bot" go run .

# --- Сводка и живой просмотр логов ----------------------------------------------

cat <<EOF

==========================================================
 ReceiptCollector debug-окружение запущено
----------------------------------------------------------
 mongo (docker)      :27017    nginx (docker)  :8080
 postgres (docker)   :5432     frontend (vite) :5173
 backend (go run)    :8888     analytics-api   :5039
   gRPC              :15000, :15001
 bot (go run)        активен
----------------------------------------------------------
 Логи:   $LOG_DIR/<сервис>.log
 PID:    $LOG_DIR/<сервис>.pid
 Повторный запуск ./dev-run.sh перезапустит сервисы разработки.
 Остановка: нажмите Ctrl+C
==========================================================
EOF

# Ctrl+C -> SIGINT -> exit -> trap cleanup -> остановка процессов этого экземпляра
tail -f "$LOG_DIR/backend.log" "$LOG_DIR/analytics-api.log" "$LOG_DIR/frontend.log" "$LOG_DIR/bot.log"