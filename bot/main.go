package main

import (
	"context"
	"log"
	"os"
	"time"

	"github.com/drypa/ReceiptCollector/bot/analytics"
	"github.com/drypa/ReceiptCollector/bot/backend"
	"github.com/drypa/ReceiptCollector/bot/backend/report"
	"github.com/drypa/ReceiptCollector/bot/backend/user"
	"github.com/drypa/ReceiptCollector/bot/commands"
	"google.golang.org/grpc/credentials"
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

	// Wait for backend gRPC to become ready with a 5-minute timeout.
	// During this time the bot retries the connection with exponential backoff.
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Minute)
	defer cancel()
	if err := grpcClient.WaitForReady(ctx); err != nil {
		log.Printf("Backend gRPC (%s) did not become ready within timeout: %v", backendGrpcAddress, err)
		os.Exit(1)
	}

	provider, err := user.New(grpcClient)
	if err != nil {
		log.Fatal(err)
	}
	analyticsClient := analytics.NewClient(options.AnalyticsUrl)
	registrar := createCommandsRegistrar(grpcClient, &provider, analyticsClient)
	err = start(options, reportsClient, registrar)
	if err != nil {
		log.Fatal(err)
	}
}

func createCommandsRegistrar(grpcClient *backend.GrpcClient, users *user.Provider, analyticsClient *analytics.Client) *commands.Registrar {
	registrar := commands.Registrar{}

	empty := commands.EmptyCommand{}
	registrar.Register(empty)

	start := commands.StartCommand{}
	registrar.Register(start)

	register := commands.NewRegisterCommand(users, grpcClient)
	registrar.Register(register)

	code := commands.NewConfirmationCodeCommand(users, grpcClient)
	registrar.Register(code)

	getReceiptReport := commands.NewGetReceiptReportCommand(users, grpcClient)
	registrar.Register(getReceiptReport)

	addReceiptCommand := commands.NewAddReceiptCommand(users, grpcClient)
	registrar.Register(addReceiptCommand)

	login := commands.NewGetLoginLinkCommand(analyticsClient)
	registrar.Register(login)

	wrongCommand := commands.WrongCommand{}
	registrar.RegisterDefault(wrongCommand)

	return &registrar
}
