package device

import (
	"context"
	api "github.com/drypa/ReceiptCollector/api/inside"
)

//Processor for actions with devices.
type Processor struct {
	r *Repository
}

func (p *Processor) GetDevices(request *api.GetDevicesRequest, server api.InternalApi_GetDevicesServer) error {
	panic("implement me")
}

func (p *Processor) UpdateDevice(ctx context.Context, request *api.UpdateDeviceRequest) (*api.ErrorResponse, error) {
	panic("implement me")
}

//NewProcessor constructs Processor.
func NewProcessor(r *Repository) *Processor {
	return &Processor{r: r}
}
