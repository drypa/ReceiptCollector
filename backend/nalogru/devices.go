package nalogru

import (
	"context"
	"receipt_collector/nalogru/device"
)

type Devices interface {
	Add(ctx context.Context, d device.Device) error
	Count(ctx context.Context) (int, error)
	Rent(ctx context.Context) (*device.Device, error)
	Update(ctx context.Context, device *device.Device) error
	Free(ctx context.Context, device *device.Device) error
}
