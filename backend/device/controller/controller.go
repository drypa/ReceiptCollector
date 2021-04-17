package controller

import (
	"context"
	"encoding/json"
	"fmt"
	"net/http"
	"receipt_collector/device"
	"receipt_collector/dispose"
	"time"
)

type Controller struct {
	service device.Devices
}

//New creates controller.
func New(service device.Devices) *Controller {
	return &Controller{service: service}
}
func (c *Controller) AddDeviceHandler(writer http.ResponseWriter, request *http.Request) {
	defer dispose.Dispose(request.Body.Close, "Error while request body close")
	ctx, cancel := context.WithTimeout(request.Context(), 10*time.Second)
	defer cancel()
	if request.Method == http.MethodPost {
		request, err := getAddDeviceRequestFromBody(request)
		if err != nil {
			onError(writer, err)
			return
		}
		d := device.Device{
			ClientSecret: request.ClientSecret,
			SessionId:    request.SessionId,
			RefreshToken: request.RefreshToken,
		}
		err = c.service.Add(ctx, d)
		if err != nil {
			onError(writer, err)
			return
		}
	}
}

func getAddDeviceRequestFromBody(request *http.Request) (AddRequest, error) {
	addRequest := AddRequest{}
	err := json.NewDecoder(request.Body).Decode(&addRequest)
	return addRequest, err
}
func onError(writer http.ResponseWriter, err error) {
	_ = fmt.Errorf("error: %v", err)
	writer.WriteHeader(http.StatusInternalServerError)
}
