#!/bin/bash

SCRIPT_DIR="$(dirname "$(readlink -f "$0")")"
source "$SCRIPT_DIR/.env"

if [ -z "$1" ]; then
  echo "Использование: $0 <каталог-дампа>  (каталоги в $MONGO_BACKUP)"
  exit 1
fi

# MongoDB: обе бизнес-базы из каталога дампа
docker exec -it  $(docker ps -f "name=mongo" -q) mongorestore --drop --gzip \
--username $MONGO_LOGIN --password $MONGO_SECRET  \
"/backup/$1"

# Analytics: база PostgreSQL `receipts` (custom-format дамп из backup.sh).
# Читается со stdin контейнера — volume /backup у receipt-postgres нет.
PG_DUMP="$MONGO_BACKUP/$1/analytics-postgres.dump"
if [ -f "$PG_DUMP" ]; then
  # На чистом PostgreSQL базы может ещё не быть — создаём до восстановления
  docker exec $(docker ps -f "name=postgres" -q) psql -U $PG_LOGIN -d postgres -tAc \
    "SELECT 1 FROM pg_database WHERE datname = 'receipts'" | grep -q 1 \
    || docker exec $(docker ps -f "name=postgres" -q) createdb -U $PG_LOGIN -O $PG_LOGIN receipts

  docker exec -i $(docker ps -f "name=postgres" -q) pg_restore -U $PG_LOGIN -d receipts \
    --clean --if-exists --no-owner < "$PG_DUMP" \
    || echo "WARNING: pg_restore завершился с ошибками (см. вывод выше)" >&2
else
  echo "WARNING: $PG_DUMP не найден — Analytics пропущен (в дампах старше этого изменения PostgreSQL нет)" >&2
fi
