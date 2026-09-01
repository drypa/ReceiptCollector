#!/bin/sh

if command -v docker-compose >/dev/null 2>&1; then
    docker-compose build
else
    docker compose build
fi


