package worker

import (
	"github.com/drypa/ReceiptCollector/kkt"
	device "receipt_collector/device/repository"
	"receipt_collector/nalogru"
	"receipt_collector/receipts"
	"receipt_collector/waste"
)

//Worker for any background job.
type Worker struct {
	nalogruClient    *kkt.Client
	repository       receipts.Repository
	deviceRepository *device.Repository
	wasteRepository  *waste.Repository
	devices          nalogru.Devices
}

//New constructs Worker.
func New(nalogruClient *kkt.Client, repository receipts.Repository, deviceRepository *device.Repository, wasteRepository *waste.Repository, devices nalogru.Devices) Worker {
	return Worker{
		//TODO: nalogruClient is not required here
		nalogruClient:    nalogruClient,
		repository:       repository,
		deviceRepository: deviceRepository,
		wasteRepository:  wasteRepository,
		devices:          devices,
	}
}
