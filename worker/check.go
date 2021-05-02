package worker

import (
	"context"
	"github.com/drypa/ReceiptCollector/worker/backend"
	"log"
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
	receipt, err := worker.backendClient.GetUnchekedQr(ctx)

	if err != nil {
		return err
	}
	if receipt == nil {
		//No unchecked receipts
		return nil
	}
	status := backend.CheckPassed
	exist, err := worker.nalogruClient.CheckReceiptExist(receipt.Qr)
	if err != nil {
		status = backend.Error
	}
	if exist == false {
		status = backend.NotFound
	}

	return worker.backendClient.UpdateStatus(ctx, receipt, status)
}
