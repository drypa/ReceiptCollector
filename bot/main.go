package main

import (
	"github.com/drypa/ReceiptCollector/bot/backend"
	"github.com/drypa/ReceiptCollector/bot/backend/report"
	"github.com/drypa/ReceiptCollector/bot/backend/user"
	"github.com/drypa/ReceiptCollector/bot/commands"
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
	provider, err := user.New(grpcClient)
	if err != nil {
		log.Fatal(err)
	}
	registrar := createCommandsRegistrar(grpcClient, &provider)
	err = start(options, reportsClient, registrar)
	if err != nil {
		log.Fatal(err)
	}
}

func createCommandsRegistrar(grpcClient *backend.GrpcClient, users *user.Provider) *commands.Registrar {
	registrar := commands.Registrar{}

	empty := commands.EmptyCommand{}
	registrar.Register(empty)

	start := commands.StartCommand{}
	registrar.Register(start)

	register := commands.NewRegisterCommand(users)
	registrar.Register(register)

	getReceiptReport := commands.NewGetReceiptReportCommand(users, grpcClient)
	registrar.Register(getReceiptReport)

	addReceiptCommand := commands.NewAddReceiptCommand(users, grpcClient)
	registrar.Register(addReceiptCommand)

	wrongCommand := commands.WrongCommand{}
	registrar.RegisterDefault(wrongCommand)

	return &registrar
}
