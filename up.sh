#!/bin/sh

echo starting containers...

cd ./docker/
docker-compose up -d
cd ../