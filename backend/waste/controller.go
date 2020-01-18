package waste

import (
	"encoding/json"
	"fmt"
	"net/http"
	"time"
)

type Controller struct {
	repository Repository
}

func New(repository Repository) Controller {
	return Controller{
		repository: repository,
	}
}

func (controller Controller) GetHandler(writer http.ResponseWriter, request *http.Request) {
	ctx := request.Context()
	defer request.Body.Close()
	filter, err := getFilterFromQuery(request)
	if err != nil {
		onError(writer, err)
		return
	}
	receipts, err := controller.repository.GetByFilter(ctx, filter)
	if err != nil {
		onError(writer, err)
		return
	}
	writeResponse(receipts, writer)
}

func onError(writer http.ResponseWriter, err error) {
	_ = fmt.Errorf("Error: %v", err)
	writer.WriteHeader(http.StatusInternalServerError)
}

func getFilterFromQuery(request *http.Request) (WasteFilter, error) {
	var filter = WasteFilter{}
	err := json.NewDecoder(request.Body).Decode(&filter)
	if err != nil {
		return filter, err
	}
	return filter, nil
}

func writeResponse(responseObject interface{}, writer http.ResponseWriter) {
	resp, err := json.Marshal(responseObject)
	if err != nil {
		onError(writer, err)
		return
	}
	_, err = writer.Write(resp)
	if err != nil {
		onError(writer, err)
		return
	}
}

type WasteFilter struct {
	UserId    string
	StartDate *time.Time
	EndDate   *time.Time
}
