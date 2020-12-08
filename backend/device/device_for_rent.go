package device

import "receipt_collector/nalogru/device"

type DeviceForRent struct {
	device.Device
	IsRent bool
}
