#!/bin/sh

echo "starting containers..."

if command -v docker-compose >/dev/null 2>&1; then
    docker-compose -f docker-compose.develop.yml -p receipt-collector-dev up -d --remove-orphans
else
    docker compose -f docker-compose.develop.yml -p receipt-collector-dev up -d --remove-orphans
fi