package workers

import (
	"context"
	"log"
	"receipt_collector/nalogru"
	"receipt_collector/receipts"
	"time"
)

type Worker struct {
	nalogruClient nalogru.Client
	repository    receipts.Repository
}

func New(nalogruClient nalogru.Client, repository receipts.Repository) Worker {
	return Worker{
		nalogruClient: nalogruClient,
		repository:    repository,
	}
}

func (worker Worker) OdfsStart(ctx context.Context, settings Settings) {
	ticker := time.NewTicker(settings.Interval)

	for {
		select {
		case <-ctx.Done():
			log.Println("Odfs request worker finished")
			return
		case <-ticker.C:
			hour := time.Now().Hour()
			if hour >= settings.Start || hour <= settings.End {
				worker.processRequests(ctx)
			} else {
				log.Print("Not Yet. Odfs request delayed.")
				break
			}
			break
		}
	}

}

func (worker Worker) processRequests(ctx context.Context) {
	usersReceipt, err := worker.repository.GetWithoutOdfsRequest(ctx)

	if err != nil {
		log.Printf("error while fetch unprocessed user requests. %s \n", err)
		return
	}
	if usersReceipt == nil {
		log.Println("No Odfs requests required")
		return
	}

	status := receipts.Success
	if usersReceipt.Receipt == nil {
		err = worker.nalogruClient.SendOdfsRequest(usersReceipt.QueryString)
		if err != nil {
			status = receipts.Error
			log.Printf("Odfs request error for query: %s. error= %v", usersReceipt.QueryString, err)
		}
	}

	err = worker.repository.UpdateOdfsStatus(ctx, *usersReceipt, status)
	check(err)
}

func check(err error) {
	if err != nil {
		log.Printf("Error occurred %v", err)
		panic(err)
	}
}
