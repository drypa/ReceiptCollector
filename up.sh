#!/bin/sh

echo starting containers...

docker-compose pull
docker-compose -p receipt-collector up -d
