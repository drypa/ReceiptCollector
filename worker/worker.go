package worker

import (
	"context"
	"github.com/drypa/ReceiptCollector/kkt"
	"github.com/drypa/ReceiptCollector/worker/backend"
)

//Worker for any background job.
type Worker struct {
	kkt           *kkt.Client
	backendClient *backend.GrpcClient
	devices       *Devices
}

//New constructs Worker.
func New(backendClient *backend.GrpcClient, devices *Devices) (Worker, error) {
	worker := Worker{
		backendClient: backendClient,
		devices:       devices,
	}
	err := setKktClient(context.Background(), &worker)
	return worker, err
}

func setKktClient(ctx context.Context, worker *Worker) error {
	devices := *(worker.devices)
	device, err := devices.Rent(ctx)
	if err != nil {
		return err
	}

	client := kkt.NewClient(baseAddress, *device)
	worker.kkt = client
	return nil

}
