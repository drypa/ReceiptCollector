#!/bin/sh

echo dep ensure...
dep ensure

echo go build...
CGO_ENABLED=0 go build -o docker/receipt_collector

echo building collector...
sh ./docker/build-collector-image.sh
#
#echo building frontend...
#chmod +x ./build-frontend.sh
#sh ./build-frontend.sh

#echo building gateway...
#sh ./docker/gateway/build.sh

cd ./docker
docker-compose build
cd ../
