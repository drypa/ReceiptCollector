#!/bin/sh

docker build $(dirname $0) -t receipt-collector-gateway:latest
rm -rf ./dist