#!/bin/bash
source .env

docker exec -it  $(docker ps -f "name=mongo" -q) mongodump --out /backup/`date +"%m-%d-%y-%H-%M-%S"` --gzip --db receipt_collection \
--username $MONGO_LOGIN --password $MONGO_SECRET --authenticationDatabase admin
