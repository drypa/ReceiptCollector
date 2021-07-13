package worker

import (
	"context"
)

//Devices to rent device.
type Devices struct {
	//TODO: should send all requests to backend
}

func (d *Devices) Rent(ctx context.Context) (*Device, error) {
	panic("not implemented")
}
func (d *Devices) Update(ctx context.Context, device *Device) error {
	panic("not implemented")
}
func (d *Devices) Free(ctx context.Context, device *Device) error {
	panic("not implemented")
}

//Device contains all API credentials.
type Device struct {
}

func (d Device) GetSecret() string {
	panic("not implemented")
}
func (d Device) GetSessionId() string {
	panic("not implemented")
}
func (d Device) GetRefreshToken() string {
	panic("not implemented")
}
func (d Device) GetId() string {
	panic("not implemented")
}
func (d Device) Refresh(newToken string, newSession string) {
	//TODO: need implement
}
