package main

import (
	"github.com/drypa/ReceiptCollector/bot/backend"
	"github.com/drypa/ReceiptCollector/bot/backend/report"
	"google.golang.org/grpc/credentials"
	"log"
	"os"
)

func main() {
	options := FromEnv()
	backendGrpcAddress := getEnvVar("BACKEND_GRPC_ADDR")
	reportsGrpcAddress := getEnvVar("REPORTS_GRPC_ADDR")
	creds, err := credentials.NewClientTLSFromFile("/usr/share/receipts/ssl/certs/certificate.crt", "")
	if err != nil {
		log.Printf("Failed to load server certificate from file. Error: %v", err)
		os.Exit(1)
	}
	grpcClient := backend.NewGrpcClient(backendGrpcAddress, creds)
	reportsClient := report.New(reportsGrpcAddress, creds)
	err = start(options, grpcClient, reportsClient)
	if err != nil {
		log.Fatal(err)
	}
}
