#/bin/sh
dep ensure
CGO_ENABLED=0 go build -o docker/receipt_collector