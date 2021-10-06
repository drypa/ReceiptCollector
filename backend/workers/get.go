package workers

import (
	"context"
	"log"
	"receipt_collector/nalogru"
	"receipt_collector/receipts"
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
				if err.Error() == nalogru.DailyLimitReached {
					ticker.Reset(getDurationToNextDay(time.Now()))
					log.Println("timer snoozed  bis tomorrow")
				}
			} else {
				ticker.Reset(settings.Interval)
			}
		}
	}
}

func getDurationToNextDay(t time.Time) time.Duration {
	tomorrow := time.Date(t.Year(), t.Month(), t.Day()+1, 0, 0, 0, 0, time.UTC)
	return tomorrow.Sub(t)

}

func (worker *Worker) getReceipt(ctx context.Context, client *nalogru.Client) error {
	receipt, err := worker.repository.GetWithoutTicket(ctx)
	if err != nil {
		log.Printf("failed to get tickets to process: %v", err)
		return err
	}

	if receipt == nil {
		//No new requests
		return nil
	}

	log.Printf("try get ticket with qr: %s\n", receipt.QueryString)
	id, err := client.GetTicketId(receipt.QueryString)

	if err != nil && err.Error() == nalogru.DailyLimitReached {
		return err
	}

	if err == nalogru.AuthError {
		err = worker.refreshSession(ctx, client)
		if err != nil {
			log.Printf("failed to refresh session. %v\n", err)
			return err
		}
		id, err = client.GetTicketId(receipt.QueryString)
	}

	if err != nil {
		log.Printf("failed get receipt id %v\n", err)
		err := worker.repository.SetReceiptStatus(ctx, receipt.Id.Hex(), receipts.Error)
		return err
	}

	err = worker.repository.SetTicketId(ctx, receipt, id)
	if err != nil {
		log.Printf("set ticket id failed: %v", err)
		return err
	}

	return worker.loadRawReceipt(ctx, id)
}

func (worker *Worker) loadRawReceipt(ctx context.Context, id string) error {
	details, err := worker.nalogruClient.GetTicketById(id)

	if err == nalogru.AuthError {
		err = worker.refreshSession(ctx, worker.nalogruClient)
		if err != nil {
			log.Printf("failed to refresh session. %v\n", err)
			return err
		}
		details, err = worker.nalogruClient.GetTicketById(id)
	}

	if err != nil {
		log.Printf("get ticket by id %s failed: %v", id, err)
		return err
	}

	err = worker.repository.InsertRawTicket(ctx, details)
	ticket := getTicketExistence(details)
	log.Printf("raw ticket %v saved. with status %d. ticket %s \n", id, details.Status, ticket)
	return err
}

func getTicketExistence(details *nalogru.TicketDetails) string {
	ticket := "exist"
	if details.Ticket == nil {
		ticket = "not exist"
	}
	return ticket
}

func (worker *Worker) refreshSession(ctx context.Context, client *nalogru.Client) error {
	err := client.RefreshSession()
	if err != nil {
		log.Printf("failed to refresh session: %v", err)
		return err
	}
	device := client.GetDevice()
	err = worker.devices.Update(ctx, &device)
	if err != nil {
		log.Printf("failed to update device: %v", err)
		return err
	}
	log.Printf("device %s updated\n", device.Id.Hex())
	return nil
}
