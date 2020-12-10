package device

import (
	"context"
	"errors"
	"receipt_collector/nalogru/device"
)

type Service struct {
	r       *Repository
	devices []DeviceForRent
}

func NewService(ctx context.Context, r *Repository) (*Service, error) {
	all, err := r.All(ctx)
	if err != nil {
		return nil, err
	}
	s := &Service{r: r}
	s.devices = make([]DeviceForRent, len(all))

	for i, v := range all {
		s.devices[i] = DeviceForRent{
			Device: v,
			IsRent: false,
		}
	}

	return s, nil
}

func (s *Service) Add(ctx context.Context, d device.Device) error {
	return s.r.Add(ctx, d)
}

func (s *Service) Count(ctx context.Context) (int, error) {
	return len(s.devices), nil
}

func (s *Service) Rent(ctx context.Context) (*device.Device, error) {
	for _, v := range s.devices {
		if v.IsRent == false {
			v.IsRent = true
			return &v.Device, nil
		}
	}
	return nil, nil
}

func (s *Service) Update(ctx context.Context, device *device.Device) error {
	return s.r.Update(ctx, device)
}

func (s *Service) Free(ctx context.Context, device *device.Device) error {
	for _, v := range s.devices {
		if device.Id == v.Id {
			v.IsRent = false
			return nil
		}
	}
	return errors.New("device not found")
}
