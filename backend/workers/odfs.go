package workers

import (
	"receipt_collector/nalogru"
	"receipt_collector/receipts"
)

type Worker struct {
	nalogruClient *nalogru.Client
	repository    receipts.Repository
}

func New(nalogruClient *nalogru.Client, repository receipts.Repository) Worker {
	return Worker{
		nalogruClient: nalogruClient,
		repository:    repository,
	}
}
