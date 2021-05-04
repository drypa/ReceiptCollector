package worker

import (
	"context"
)

//Devices interface to rent device.
type Devices interface {
	Rent(ctx context.Context) (*Device, error)
	Update(ctx context.Context, device *Device) error
	Free(ctx context.Context, device *Device) error
}

//Device contains all API credentials.
type Device interface {
	GetSecret() string
	GetSessionId() string
	GetRefreshToken() string
	GetId() string
	Refresh(newToken string, newSession string)
}
