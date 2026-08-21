#!/bin/sh

echo "starting containers..."

if command -v docker-compose >/dev/null 2>&1; then
    docker-compose pull
    docker-compose -p receipt-collector up -d --remove-orphans
else
    docker compose pull
    docker compose -p receipt-collector up -d --remove-orphans
fi