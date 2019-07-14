#!/bin/sh

echo starting containers...

cd ./docker/
docker-compose -f docker-compose.develop.yml -p receipt-collector up -d
cd ../