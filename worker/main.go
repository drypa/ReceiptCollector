package worker

import (
	"context"
	"log"
	"os"
	"receipt_collector/nalogru"
)

func main() {
	log.SetOutput(os.Stdout)
	ctx := context.Background()
	settings := ReadFromEnvironment()
	log.Printf("Worker settings %v \n", settings)

	nalogruClient := nalogru.NewClient(baseAddress, d)

	w := New(nalogruClient, receiptRepository, deviceRepository, &wasteRepository, deviceService)

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
