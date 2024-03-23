package device

import "receipt_collector/nalogru/device"

type ForRent struct {
	*device.Device
	IsRent bool
}
