package workers

import (
	repository2 "receipt_collector/device/repository"
	"receipt_collector/nalogru"
	"receipt_collector/receipts"
	"receipt_collector/waste"
)

type Worker struct {
	nalogruClient    *nalogru.Client
	repository       receipts.Repository
	deviceRepository *repository2.Repository
	wasteRepository  *waste.Repository
	devices          nalogru.Devices
}

//New constructs Worker.
func New(nalogruClient *nalogru.Client, repository receipts.Repository, deviceRepository *repository2.Repository, wasteRepository *waste.Repository, devices nalogru.Devices) Worker {
	return Worker{
		//TODO: nalogruClient is not required here
		nalogruClient:    nalogruClient,
		repository:       repository,
		deviceRepository: deviceRepository,
		wasteRepository:  wasteRepository,
		devices:          devices,
	}
}
