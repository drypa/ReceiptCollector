package workers

import (
	"receipt_collector/nalogru"
	"receipt_collector/receipts"
)

type Worker struct {
	nalogruClient *nalogru.Client
	repository    receipts.Repository
}

//New constructs Worker.
func New(nalogruClient *nalogru.Client, repository receipts.Repository) Worker {
	return Worker{
		nalogruClient: nalogruClient,
		repository:    repository,
	}
}
