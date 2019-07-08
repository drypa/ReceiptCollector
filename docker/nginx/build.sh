#!/bin/sh

docker build --no-cache -f $(dirname $0)/Dockerfile.dev -t nginx-gateway:dev .