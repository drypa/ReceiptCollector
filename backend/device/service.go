package device

import (
	"context"
	"receipt_collector/nalogru/device"
)

type Service struct {
	r *Repository
}

func NewService(r *Repository) *Service {
	return &Service{r: r}
}

func (s Service) Add(d device.Device, ctx context.Context) error {
	panic("implement me")
}

func (s Service) Count(ctx context.Context) (int, error) {
	panic("implement me")
}

func (s Service) RentDevice(ctx context.Context) (*device.Device, error) {
	panic("implement me")
}

func (s Service) UpdateDevice(sessionId string, refreshToken string, ctx context.Context) error {
	panic("implement me")
}

func (s Service) FreeDevice(device *device.Device, ctx context.Context) error {
	panic("implement me")
}
