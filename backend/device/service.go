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

func (s *Service) Add(ctx context.Context, d device.Device) error {
	return s.r.Add(ctx, d)
}

func (s *Service) Count(ctx context.Context) (int, error) {
	panic("implement me")
}

func (s *Service) Rent(ctx context.Context) (*device.Device, error) {
	panic("implement me")
}

func (s *Service) Update(ctx context.Context, sessionId string, refreshToken string) error {
	panic("implement me")
}

func (s *Service) Free(ctx context.Context, device *device.Device) error {
	panic("implement me")
}
