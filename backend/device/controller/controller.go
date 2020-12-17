package controller

import (
	"context"
	"encoding/json"
	"fmt"
	"net/http"
	"receipt_collector/dispose"
	"receipt_collector/nalogru"
	nalogDevice "receipt_collector/nalogru/device"
	"time"
)

type Controller struct {
	service nalogru.Devices
}

func NewController(service nalogru.Devices) *Controller {
	return &Controller{service: service}
}
func (c *Controller) AddDeviceHandler(writer http.ResponseWriter, request *http.Request) {
	defer dispose.Dispose(request.Body.Close, "Error while request body close")
	ctx, _ := context.WithTimeout(request.Context(), 10*time.Second)
	if request.Method == http.MethodPost {
		request, err := getAddDeviceRequestFromBody(request)
		if err != nil {
			onError(writer, err)
			return
		}
		d := nalogDevice.Device{
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
