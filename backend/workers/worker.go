package workers

import (
	"receipt_collector/nalogru"
	"receipt_collector/receipts"
	"receipt_collector/waste"
)

// Worker for any background job.
type Worker struct {
	nalogruClient   *nalogru.Client
	repository      receipts.Repository
	wasteRepository *waste.Repository
	devices         nalogru.Devices
}

// New constructs Worker.
func New(nalogruClient *nalogru.Client, repository receipts.Repository, wasteRepository *waste.Repository, devices nalogru.Devices) Worker {
	return Worker{
		nalogruClient:   nalogruClient,
		repository:      repository,
		wasteRepository: wasteRepository,
		devices:         devices,
	}
}
