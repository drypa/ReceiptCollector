package worker

import (
	"github.com/drypa/ReceiptCollector/kkt"
	"github.com/drypa/ReceiptCollector/worker/backend"
)

//Worker for any background job.
type Worker struct {
	nalogruClient *kkt.Client
	backendClient *backend.GrpcClient
	devices       Devices
}

//New constructs Worker.
func New(nalogruClient *kkt.Client, backendClient *backend.GrpcClient, devices Devices) Worker {
	return Worker{
		//TODO: nalogruClient is not required here
		nalogruClient: nalogruClient,
		backendClient: backendClient,
		devices:       devices,
	}
}
