package workers

import (
	"context"
	"log"
	"time"
)

//UpdateStart fetch tickets with wrong status.
func (worker *Worker) UpdateStart(ctx context.Context, settings Settings) {
	ticker := time.NewTicker(settings.Interval)

	d, err := worker.devices.Rent(ctx)

	if err != nil {
		log.Println("Failed to rent device")
	}

	//client := nalogru.NewClient(worker.nalogruClient.BaseAddress, d)
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
			worker.repository.GetRawReceiptWithoutTicket(ctx)
		}

	}
}
