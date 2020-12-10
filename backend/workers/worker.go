package workers

import (
	repository2 "receipt_collector/device/repository"
	"receipt_collector/nalogru"
	"receipt_collector/receipts"
)

type Worker struct {
	nalogruClient    *nalogru.Client
	repository       receipts.Repository
	deviceRepository *repository2.Repository
}

//New constructs Worker.
func New(nalogruClient *nalogru.Client,
	repository receipts.Repository,
	deviceRepository *repository2.Repository) Worker {
	return Worker{
		nalogruClient:    nalogruClient,
		repository:       repository,
		deviceRepository: deviceRepository,
	}
}
