package workers

import (
	"receipt_collector/device"
	"receipt_collector/nalogru"
	"receipt_collector/receipts"
)

type Worker struct {
	nalogruClient    *nalogru.Client
	repository       receipts.Repository
	deviceRepository *device.Repository
}

//New constructs Worker.
func New(nalogruClient *nalogru.Client,
	repository receipts.Repository,
	deviceRepository *device.Repository) Worker {
	return Worker{
		nalogruClient:    nalogruClient,
		repository:       repository,
		deviceRepository: deviceRepository,
	}
}
