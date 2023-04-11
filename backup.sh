#!/bin/bash

SCRIPT_DIR="$(dirname "$(readlink -f "$0")")"
source $SCRIPT_DIR/.env

docker exec  $(docker ps -f "name=mongo" -q) mongodump --out /backup/`date +"%Y-%m-%d-%H-%M-%S"` --gzip --db receipt_collection \
--username $MONGO_LOGIN --password $MONGO_SECRET --authenticationDatabase admin
