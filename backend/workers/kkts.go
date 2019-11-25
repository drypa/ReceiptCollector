package workers

import (
	"context"
	"go.mongodb.org/mongo-driver/mongo"
	"log"
	"receipt_collector/nalogru"
	"receipt_collector/receipts"
	"time"
)

func GetReceiptWorkerStart(ctx context.Context, nalogruClient nalogru.Client, mongoClient *mongo.Client, settings Settings) {
	ticker := time.NewTicker(settings.Interval)

	for {
		select {
		case <-ctx.Done():
			log.Println("Kkt request worker finished")
			return
		case <-ticker.C:
			hour := time.Now().Hour()
			if hour >= settings.Start || hour <= settings.End {
				getReceipt(ctx, mongoClient, nalogruClient)
			} else {
				log.Print("Not Yet. Kkts request delayed.")
				break
			}
			break
		}
	}
}

func getReceipt(ctx context.Context, mongoClient *mongo.Client, nalogruClient nalogru.Client) {
	receiptRepository := receipts.NewRepository(mongoClient)

	request := receiptRepository.FindOneOdfsRequestedWithoutReceipt(ctx)

	if request == nil {
		log.Println("No Kkt requests required")
		return
	}
	log.Printf("Kkt request for queryString: %s\n", request.QueryString)

	receiptBytes, err := nalogruClient.SendKktsRequest(request.QueryString)
	check(err)
	receipt, err := receipts.ParseReceipt(receiptBytes)
	if err != nil {
		body := string(receiptBytes)
		log.Printf("Can not parse response body.Body: '%s'.Error: %v", body, err)
		err := receiptRepository.ResetOdfsRequestForReceipt(ctx, request.Id.Hex())
		check(err)
		return
	}
	err = receiptRepository.SetReceipt(ctx, request.Id, receipt)
	check(err)
}
