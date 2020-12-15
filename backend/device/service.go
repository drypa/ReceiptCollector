package device

import (
	"context"
	"errors"
	"receipt_collector/device/repository"
	"receipt_collector/nalogru/device"
)

type Service struct {
	r       *repository.Repository
	devices []ForRent
}

func NewService(ctx context.Context, r *repository.Repository) (*Service, error) {
	all, err := r.All(ctx)
	if err != nil {
		return nil, err
	}
	s := &Service{r: r}
	s.devices = make([]ForRent, len(all))

	for i, v := range all {
		s.devices[i] = ForRent{
			Device: v,
			IsRent: false,
		}
	}

	return s, nil
}

func (s *Service) Add(ctx context.Context, d device.Device) error {
	for _, v := range s.devices {
		if v.ClientSecret == d.ClientSecret {
			return errors.New("that device already added")
		}
	}
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
