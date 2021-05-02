package worker

import (
	"context"
	"github.com/drypa/ReceiptCollector/kkt"
	"github.com/drypa/ReceiptCollector/worker/backend"
	"log"
	"os"
)

func main() {
	log.SetOutput(os.Stdout)
	ctx := context.Background()
	settings := ReadFromEnvironment()
	log.Printf("Worker settings %v \n", settings)

	nalogruClient := kkt.NewClient(baseAddress, d)
	backendClient := backend.NewClient()

	w := New(nalogruClient, backendClient, deviceService)

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
