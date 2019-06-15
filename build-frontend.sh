#!/bin/sh

npm run build-prod --prefix ./webapp
mv $(dirname $0)/webapp/dist $(dirname $0)/docker/gateway



