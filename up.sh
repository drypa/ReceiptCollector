#!/bin/sh

echo starting containers...

cd ./docker/
docker-compose -p receipt-collector up -d
cd ../