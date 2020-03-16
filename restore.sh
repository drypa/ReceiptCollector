#!/bin/bash

source .env

docker exec -it  $(docker ps -f "name=mongo" -q) mongorestore --drop --gzip \
--username $MONGO_LOGIN --password $MONGO_SECRET  \
"/backup/$1"
