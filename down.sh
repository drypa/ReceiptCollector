#!/bin/sh

echo stoping containers...

cd ./docker/
docker-compose down
cd ../