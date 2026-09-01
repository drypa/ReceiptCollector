#!/bin/sh

echo stoping containers...


if command -v docker-compose >/dev/null 2>&1; then
    docker-compose -p receipt-collector-dev down
else
    docker compose -p receipt-collector-dev down
fi