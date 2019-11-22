#!/bin/sh
source .env

docker exec -it  $(docker ps -f "name=mongo" -q) mongodump --out /backup --db receipt_collection \
--username $MONGO_LOGIN --password $MONGO_SECRET --authenticationDatabase admin
