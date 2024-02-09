package device

import (
	"context"
	"errors"
	"receipt_collector/device/repository"
	"receipt_collector/nalogru/device"
)

// Service to manage devices.
type Service struct {
	r       *repository.Repository
	devices []ForRent
}

// NewService creates instance of Service.
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

// Add adds new device.
func (s *Service) Add(ctx context.Context, d *device.Device) error {
	//TODO: may be add some checks
	d.Update = s.updateDeviceFunc(ctx, *d)

	forRent := ForRent{
		Device: *d,
		IsRent: false,
	}
	s.devices = append(s.devices, forRent)
	return s.r.Add(ctx, d)
}

// Count returns devices count.
func (s *Service) Count(ctx context.Context) (int, error) {
	if ctx.Err() != nil {
		return -1, ctx.Err()
	}
	return len(s.devices), nil
}

// Rent any device.
func (s *Service) Rent(ctx context.Context) (*device.Device, error) {
	if ctx.Err() != nil {
		return nil, ctx.Err()
	}
	for _, v := range s.devices {
		if v.IsRent == false {
			s.rent(&v)
			return &v.Device, nil
		}
	}
	return nil, errors.New("no available devices found")
}

// RentDevice rent concrete device.
func (s *Service) RentDevice(ctx context.Context, d *device.Device) error {
	if ctx.Err() != nil {
		return ctx.Err()
	}
	for _, v := range s.devices {
		if v.Id == d.Id {
			if v.IsRent {
				return errors.New("device is already used")
			} else {
				s.rent(&v)
			}
		}
	}
	return errors.New("device not found")
}

func (s *Service) rent(v *ForRent) {
	v.IsRent = true
}

func (s *Service) Update(ctx context.Context, device *device.Device, sessionId string, refreshToken string) error {
	device.SessionId = sessionId
	device.RefreshToken = refreshToken
	return s.r.Update(ctx, device)
}

// Free release the rented device
func (s *Service) Free(_ context.Context, device *device.Device) error {
	for _, v := range s.devices {
		if device.Id == v.Id {
			v.IsRent = false
			return nil
		}
	}
	return errors.New("device not found")
}

// All return all registered devices
func (s *Service) All(_ context.Context) []*device.Device {
	res := make([]*device.Device, len(s.devices))
	for i, d := range s.devices {
		res[i] = &d.Device
	}
	return res
}

func (s *Service) GetByUserId(ctx context.Context, userId string) (*device.Device, error) {
	devices, err := s.r.All(ctx)
	if err != nil {
		return nil, err
	}
	for _, d := range devices {
		if d.UserId == userId {
			d.Update = s.updateDeviceFunc(ctx, d)
			return &d, nil
		}
	}
	return nil, nil
}

func (s *Service) updateDeviceFunc(ctx context.Context, d device.Device) func(sessionId string, refreshToken string) error {
	return func(sessionId string, refreshToken string) error {
		return s.Update(ctx, &d, sessionId, refreshToken)
	}
}
