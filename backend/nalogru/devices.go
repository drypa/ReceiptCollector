package nalogru

import (
	"context"
	"receipt_collector/nalogru/device"
)

type Devices interface {
	Add(ctx context.Context, d *device.Device) error
	Count(ctx context.Context) (int, error)
	Rent(ctx context.Context) (*device.Device, error)
	RentDevice(ctx context.Context, d *device.Device) error
	Update(ctx context.Context, device *device.Device, sessionId string, refreshToken string) error
	Free(ctx context.Context, device *device.Device) error
	All(ctx context.Context) []*device.Device
	GetByUserId(ctx context.Context, userId string) (*device.Device, error)
}
