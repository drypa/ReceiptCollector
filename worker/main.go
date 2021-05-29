package worker

import (
	"context"
	"github.com/drypa/ReceiptCollector/worker/backend"
	"google.golang.org/grpc/credentials"
	"log"
	"os"
)

var baseAddress = os.Getenv("NALOGRU_BASE_ADDR")
var backendGrpcAddress = os.Getenv("BACKEND_GRPC_ADDR")

func main() {
	log.SetOutput(os.Stdout)
	ctx := context.Background()
	settings := ReadFromEnvironment()
	log.Printf("Worker settings %v \n", settings)

	creds, err := credentials.NewClientTLSFromFile("/usr/share/receipts/ssl/certs/certificate.pem", "")
	if err != nil {
		log.Printf("Failed to load server certificate from file. Error: %v", err)
		os.Exit(1)
	}
	backendClient := backend.NewClient(backendGrpcAddress, creds)

	w, err := New(backendClient, deviceService)

	if err != nil {
		log.Fatal(err)
	}

	go w.CheckReceiptStart(ctx, settings)
	go w.GetReceiptStart(ctx, settings)
	//go worker.UpdateRawReceiptStart(ctx, settings)

	//wasteWorker := waste.NewWorker()
	//go func() {
	//	var err = wasteWorker.Process(ctx, client)
	//	if err != nil {
	//		log.Fatal(err)
	//	}
	//}()
}
