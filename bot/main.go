package main

import (
	"github.com/drypa/ReceiptCollector/bot/backend"
	"log"
)

func main() {
	options := FromEnv()
	backendUrl := getEnvVar("BACKEND_URL")
	client := backend.New(backendUrl)
	err := start(options, client)
	if err != nil {
		log.Fatal(err)
	}
}
