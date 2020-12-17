package workers

import (
	"context"
	"log"
	"receipt_collector/nalogru"
	"time"
)

//GetReceiptStart starts get receipt worker.
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
				log.Printf("Get receipt error: %v\n", err)
			}
		}
	}
}

func (worker *Worker) getReceipt(ctx context.Context, client *nalogru.Client) error {
	receipt, err := worker.repository.GetWithoutTicket(ctx)
	if err != nil {
		log.Printf("failed to get tickets to process: %v", err)
		return err
	}

	if receipt == nil {
		log.Println("No new requests")
		return nil
	}

	id, err := client.GetTicketId(receipt.QueryString)
	if err != nil {
		if err == nalogru.AuthError {
			d, err := client.RefreshSession()
			if err != nil {
				log.Printf("failed to refresh session: %v", err)
				return err
			}
			err = worker.devices.Update(ctx, d)
			if err != nil {
				log.Printf("failed to update device: %v", err)
				return err
			}
		}

		return err
	}
	err = worker.repository.SetTicketId(ctx, receipt, id)
	if err != nil {
		log.Printf("set ticket id failed: %v", err)
		return err
	}

	details, err := client.GetTicketById(id)
	if err != nil {
		log.Printf("get ticket by id %s failed: %v", id, err)
		return err
	}

	return worker.repository.InsertRawTicket(ctx, details)
}
