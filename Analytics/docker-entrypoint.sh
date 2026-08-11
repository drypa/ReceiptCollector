#!/bin/sh
set -e

# Синхронизация собранного фронтенда в общий том для nginx (read-only на стороне nginx)
if [ -d /app/api/wwwroot ] && [ -d /srv/wwwroot ]; then
    rm -rf /srv/wwwroot/*
    cp -r /app/api/wwwroot/. /srv/wwwroot/
fi

exec dotnet /app/api/ReceiptCollector.Analytics.Api.dll
