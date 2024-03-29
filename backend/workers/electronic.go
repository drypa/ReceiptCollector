package workers

import (
	"context"
	"github.com/go-co-op/gocron"
	"go.mongodb.org/mongo-driver/bson/primitive"
	"log"
	"receipt_collector/nalogru"
	"receipt_collector/nalogru/qr"
	"receipt_collector/receipts"
	"time"
)

// GetElectronicReceiptStart receives electronic receipts for all devices.
func (worker *Worker) GetElectronicReceiptStart(ctx context.Context) {
	if ctx.Err() != nil {
		return
	}
	s := gocron.NewScheduler(time.Local)

	_, err := s.Every(1).Day().At("01:00").Do(worker.getElectronic, ctx)
	if err != nil {
		log.Printf("failed to create job %v\n", err)
	}

	s.StartAsync()
}
func (worker *Worker) getElectronic(ctx context.Context) {
	for _, d := range worker.devices.All(ctx) {
		tickets, err := worker.nalogruClient.GetElectronicTickets(d)
		if err != nil {
			log.Printf("Failed to get electronic tickets: %v\n", err)
			return
		}
		err = worker.insertTicketsIfNeeded(ctx, tickets)
		if err != nil {
			log.Printf("Failed to insert electronic tickets: %v\n", err)
			return
		}
	}
}
func (worker *Worker) insertTicketsIfNeeded(ctx context.Context, tickets []*nalogru.TicketDetails) error {
	for _, t := range tickets {
		query, err := qr.Parse(t.Qr)
		if err != nil {
			log.Printf("Failed to parse '%s'\n", t.Qr)
			return err
		}
		receipt, err := worker.repository.GetAllOwnersByQueryString(ctx, query.ToString())
		if err != nil {
			log.Printf("Failed to get receipts by qr '%s'\n", t.Qr)
			return err
		}
		if receipt == nil {
			receiptRequest := receipts.UsersReceipt{
				Owner:       primitive.NilObjectID, //TODO: owner needed
				QueryString: query.ToString(),
				//Without ticketId would process by get-worker
			}
			err := worker.repository.Insert(ctx, receiptRequest)
			if err != nil {
				log.Printf("Failed to insert receipt request for %s", query.ToString()) //TODO: add owner to output
				return err
			}
		}
	}
	return nil
}
