package device

import (
	"context"
	api "github.com/drypa/ReceiptCollector/api/inside"
	"go.mongodb.org/mongo-driver/bson/primitive"
)

//Processor for actions with devices.
type Processor struct {
	r *Repository
}

func (p *Processor) GetDevices(request *api.GetDevicesRequest, server api.InternalApi_GetDevicesServer) error {
	devices, err := p.r.All(server.Context())
	if err != nil {
		return err
	}
	for _, v := range devices {
		err := server.Send(&api.Device{
			ClientSecret: v.ClientSecret,
			SessionId:    v.SessionId,
			RefreshToken: v.RefreshToken,
			Id:           v.Id.Hex(),
		})
		if err != nil {
			return err
		}
	}
	return nil
}

func (p *Processor) UpdateDevice(ctx context.Context, request *api.UpdateDeviceRequest) (*api.ErrorResponse, error) {
	id, err := primitive.ObjectIDFromHex(request.Device.Id)
	if err != nil {
		return nil, err
	}
	device := Device{
		ClientSecret: request.Device.ClientSecret,
		SessionId:    request.Device.SessionId,
		RefreshToken: request.Device.RefreshToken,
		Id:           id,
	}
	err = p.r.Update(ctx, &device)
	return nil, err
}

//NewProcessor constructs Processor.
func NewProcessor(r *Repository) *Processor {
	return &Processor{r: r}
}
