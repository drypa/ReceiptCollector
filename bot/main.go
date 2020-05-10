package main

import (
	"github.com/drypa/ReceiptCollector/bot/backend"
	"google.golang.org/grpc/credentials"
	"log"
	"os"
)

func main() {
	options := FromEnv()
	backendUrl := getEnvVar("BACKEND_URL")
	backendGrpcAddress := getEnvVar("BACKEND_GRPC_ADDR")
	client := backend.New(backendUrl)
	creds, err := credentials.NewClientTLSFromFile("../ssl/certificate.crt", "")
	if err != nil {
		log.Printf("Failed to load server certificate from file. Error: %v", err)
		os.Exit(1)
	}
	grpcClient := backend.NewGrpcClient(backendGrpcAddress, creds)
	err = start(options, client, grpcClient)
	if err != nil {
		log.Fatal(err)
	}
}
