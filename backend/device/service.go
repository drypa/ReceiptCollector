package device

import (
	"context"
	"errors"
)

//Service to manage devices.
type Service struct {
	r       *Repository
	devices []ForRent
}

//NewService creates instance of Service.
func NewService(ctx context.Context, r *Repository) (*Service, error) {
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

//Add adds new device.
func (s *Service) Add(ctx context.Context, d Device) error {
	for _, v := range s.devices {
		if v.GetSecret() == d.GetSecret() {
			return errors.New("that device already added")
		}
	}
	return s.r.Add(ctx, d)
}

//Count returns devices count.
func (s *Service) Count(ctx context.Context) (int, error) {
	return len(s.devices), nil
}

//Rent device.
func (s *Service) Rent(ctx context.Context) (*Device, error) {
	for _, v := range s.devices {
		if v.IsRent == false {
			v.IsRent = true
			return &v.Device, nil
		}
	}
	return nil, errors.New("no available devices found")
}

func (s *Service) Update(ctx context.Context, device *Device) error {
	return s.r.Update(ctx, device)
}

func (s *Service) Free(ctx context.Context, device *Device) error {
	for _, v := range s.devices {
		if device.GetId() == v.GetId() {
			v.IsRent = false
			return nil
		}
	}
	return errors.New("device not found")
}
