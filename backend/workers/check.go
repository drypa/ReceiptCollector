package workers

import (
	"context"
	"log"
	"receipt_collector/receipts"
	"time"
)

//CheckReceiptStart starts check all unchecked receipts task.
func (worker Worker) CheckReceiptStart(ctx context.Context, settings Settings) {
	ticker := time.NewTicker(settings.Interval)

	for {
		select {
		case <-ctx.Done():
			log.Println("Check request worker finished")
			return
		case <-ticker.C:
			err := worker.checkReceipt(ctx)
			if err != nil {
				log.Println("Check request error")
			}
		}
	}
}

func (worker Worker) checkReceipt(ctx context.Context) error {
	receipt, err := worker.repository.GetWithoutCheckRequest(ctx)

	if err != nil {
		return err
	}
	if receipt == nil {
		//No unchecked receipts
		return nil
	}
	status := receipts.Success
	exist, err := worker.nalogruClient.CheckReceiptExist(receipt.QueryString)
	if err != nil {
		status = receipts.Error
	}
	if exist == false {
		status = receipts.NotFound
	}

	return worker.repository.UpdateCheckStatus(ctx, *receipt, status)
}
