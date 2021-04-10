package workers

import (
	"context"
	"log"
	"time"
)

//UpdateRawReceiptStart fetch tickets with wrong status.
func (worker *Worker) UpdateRawReceiptStart(ctx context.Context, settings Settings) {
	ticker := time.NewTicker(settings.Interval)

	d, err := worker.devices.Rent(ctx)

	if err != nil {
		log.Println("Failed to rent device")
	}

	for {
		select {
		case <-ctx.Done():
			log.Println("Get receipt worker finished")
			err := worker.devices.Free(context.Background(), d)
			if err != nil {
				log.Println("Failed to Free used device")
			}
			return
		case <-ticker.C:
			receipt, err := worker.repository.GetRawReceiptWithoutTicket(ctx)
			if err != nil {
				log.Println("Failed to get receipt")
			}
			if receipt == nil {
				break
			}
			err = worker.loadRawReceipt(ctx, receipt.Id)
			if err != nil {
				log.Println("Failed to reload raw ticker")
			}
		}

	}
}
