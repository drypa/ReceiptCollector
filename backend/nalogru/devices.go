package nalogru

import (
	"context"
	"receipt_collector/nalogru/device"
)

type Devices interface {
	Add(d device.Device, ctx context.Context) error
	Count(ctx context.Context) (int, error)
	RentDevice(ctx context.Context) (*device.Device, error)
	UpdateDevice(sessionId string, refreshToken string, ctx context.Context) error
	FreeDevice(device *device.Device, ctx context.Context) error
}
