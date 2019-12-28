package workers

import (
	"context"
	"log"
	"receipt_collector/nalogru"
	"receipt_collector/receipts"
	"time"
)

func (worker Worker) GetReceiptStart(ctx context.Context, settings Settings) {
	ticker := time.NewTicker(settings.Interval)

	for {
		select {
		case <-ctx.Done():
			log.Println("Kkt request worker finished")
			return
		case <-ticker.C:
			hour := time.Now().Hour()
			if hour >= settings.Start || hour <= settings.End {
				worker.getReceipt(ctx)
			} else {
				log.Print("Not Yet. Kkts request delayed.")
				break
			}
			break
		}
	}
}

func (worker Worker) getReceipt(ctx context.Context) {
	request, err := worker.repository.FindOneOdfsRequestedWithoutReceipt(ctx)
	check(err)

	if request == nil {
		log.Println("No Kkt requests required")
		return
	}
	log.Printf("Kkt request for queryString: %s\n", request.QueryString)

	receiptBytes, err := worker.nalogruClient.SendKktsRequest(request.QueryString)
	if err != nil {
		if err.Error() == nalogru.TicketNotFound {
			worker.repository.SetKktsRequestStatus(ctx, request.Id.Hex(), receipts.NotFound)
			return
		}
		check(err)
	}
	receipt, err := receipts.ParseReceipt(receiptBytes)
	if err != nil {
		body := string(receiptBytes)
		log.Printf("Can not parse response body.Body: '%s'.Error: %v", body, err)
		err := worker.repository.ResetOdfsRequestForReceipt(ctx, request.Id.Hex())
		check(err)
		return
	}
	err = worker.repository.SetReceipt(ctx, request.Id, receipt)
	check(err)
}
