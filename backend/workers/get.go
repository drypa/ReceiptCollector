package workers

import (
	"context"
	"log"
	"receipt_collector/nalogru"
	"time"
)

func (worker *Worker) GetReceiptStart(ctx context.Context, settings Settings) {
	ticker := time.NewTicker(settings.Interval)

	d, err := worker.devices.Rent(ctx)
	if err != nil {
		log.Println("Failed to rent device")
	}

	client := nalogru.NewClient(worker.nalogruClient.BaseAddress, d)

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
			err := worker.getReceipt(ctx, client)
			if err != nil {
				log.Println("Get receipt error")
			}
		}
	}
}

func (worker *Worker) getReceipt(ctx context.Context, client *nalogru.Client) error {
	receipt, err := worker.repository.GetWithoutTicket(ctx)
	if err != nil {
		return err
	}

	if receipt == nil {
		log.Println("No new requests")
		return nil
	}

	id, err := client.GetTicketId(receipt.QueryString)
	if err != nil {
		return err
	}
	err = worker.repository.SetTicketId(ctx, receipt, id)
	if err != nil {
		return err
	}

	details, err := client.GetTicketById(id)
	if err != nil {
		return err
	}

	return worker.repository.InsertRawTicket(ctx, details)
}
