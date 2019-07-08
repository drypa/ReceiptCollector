#!/bin/sh

echo starting containers...

cd ./docker/
docker-compose -f docker-compose.develop.yml up -d
cd ../