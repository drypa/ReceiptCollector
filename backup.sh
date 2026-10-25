#!/bin/bash

SCRIPT_DIR="$(dirname "$(readlink -f "$0")")"
source $SCRIPT_DIR/.env

# Дампим все бизнес-базы: receipt_collection и receipt-data (в один каталог дампа)
BACKUP_NAME=`date +"%Y-%m-%d-%H-%M-%S"`

# Каталог дампа создаём на хосте: в него пишут и контейнер mongo (/backup — это
# ${MONGO_BACKUP}), и хостовый редирект для pg_dump ниже
mkdir -p "$MONGO_BACKUP/$BACKUP_NAME"

docker exec  $(docker ps -f "name=mongo" -q) mongodump --out /backup/$BACKUP_NAME --gzip --db receipt_collection \
--username $MONGO_LOGIN --password $MONGO_SECRET --authenticationDatabase admin

docker exec  $(docker ps -f "name=mongo" -q) mongodump --out /backup/$BACKUP_NAME --gzip --db receipt-data \
--username $MONGO_LOGIN --password $MONGO_SECRET --authenticationDatabase admin

# Analytics: база PostgreSQL `receipts` (docker-compose.yml, ConnectionString).
# У receipt-postgres нет volume /backup, поэтому custom-format дамп идёт
# потоком через stdout контейнера в тот же каталог дампа.
docker exec $(docker ps -f "name=postgres" -q) pg_dump --format=custom -U $PG_LOGIN receipts \
  > "$MONGO_BACKUP/$BACKUP_NAME/analytics-postgres.dump" \
  || { echo "ERROR: pg_dump базы Analytics (receipts) завершился с ошибкой" >&2; exit 1; }

docker exec  $(docker ps -f "name=mongo" -q) find /backup/ -maxdepth 1 -type d -mtime +365 -exec rm -rf {} \;
