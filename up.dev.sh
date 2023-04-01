#!/bin/sh

echo starting containers...

docker-compose -f docker-compose.develop.yml -p receipt-collector-dev up -d --remove-orphans
