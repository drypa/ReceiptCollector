package device

import (
	"context"
)

type Devices interface {
	Add(ctx context.Context, d Device) error
	Count(ctx context.Context) (int, error)
	Rent(ctx context.Context) (*Device, error)
	Update(ctx context.Context, device *Device) error
	Free(ctx context.Context, device *Device) error
}
