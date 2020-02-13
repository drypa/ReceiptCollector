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

func (worker Worker) getReceipt(ctx context.Context) error {
	request, err := worker.repository.FindOneOdfsRequestedWithoutReceipt(ctx)
	check(err)

	if request == nil {
		log.Println("No Kkt requests required")
		return nil
	}
	log.Printf("Kkt request for queryString: %s\n", request.QueryString)

	receiptBytes, err := worker.nalogruClient.SendKktsRequest(request.QueryString)
	if err != nil {
		switch err.Error() {
		case nalogru.TicketNotFound:
			err := worker.repository.SetKktsRequestStatus(ctx, request.Id.Hex(), receipts.NotFound)
			if err != nil {
				return err
			}
		}
		if err.Error() == nalogru.NotReadyYet {
			log.Printf("receipt '%s' is not ready yet", request.QueryString)
			return nil

		}
		check(err)
	}
	receipt, err := receipts.ParseReceipt(receiptBytes)
	if err != nil {
		body := string(receiptBytes)
		log.Printf("Can not parse response body.Body: '%s'.Error: %v", body, err)
		return err
	}
	err = worker.repository.SetReceipt(ctx, request.Id, receipt)
	if err != nil {
		return err
	}
	return nil
}
