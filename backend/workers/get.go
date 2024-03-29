package workers

import (
	"context"
	"log"
	"receipt_collector/nalogru"
	"receipt_collector/nalogru/device"
	"receipt_collector/nalogru/qr"
	"receipt_collector/receipts"
	"time"
)

// GetReceiptStart starts get receipt worker.
func (worker *Worker) GetReceiptStart(ctx context.Context, settings Settings) {
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
			err := worker.getReceipt(ctx)
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

func (worker *Worker) getReceipt(ctx context.Context) error {
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
	query, err := qr.Parse(receipt.QueryString)

	if err != nil {
		return err
	}
	normalizedQr := query.ToString()
	device, err := worker.devices.Rent(ctx)
	defer worker.devices.Free(ctx, device)
	id, err := worker.nalogruClient.GetTicketId(normalizedQr, device)

	if err != nil && err.Error() == nalogru.DailyLimitReached {
		return err
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

	return worker.loadRawReceipt(ctx, id, device)
}

func (worker *Worker) loadRawReceipt(ctx context.Context, id string, device *device.Device) error {
	details, err := worker.nalogruClient.GetTicketById(id, device)

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

//func (worker *Worker) refreshSession(ctx context.Context) error {
//	err := worker.nalogruClient.RefreshSession()
//	if err != nil {
//		log.Printf("failed to refresh session: %v", err)
//		return err
//	}
//	device := worker.nalogruClient.GetDevice()
//	err = worker.devices.Update(ctx, &device)
//	if err != nil {
//		log.Printf("failed to update device: %v", err)
//		return err
//	}
//	log.Printf("device %s updated\n", device.Id.Hex())
//	return nil
//}
