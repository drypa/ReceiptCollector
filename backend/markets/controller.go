package markets

import (
	"encoding/json"
	"fmt"
	"github.com/gorilla/mux"
	"net/http"
)

type Controller struct {
	repository Repository
}

func New(repository Repository) Controller {
	return Controller{
		repository: repository,
	}
}

func (controller Controller) MarketsBaseHandler(writer http.ResponseWriter, request *http.Request) {
	if request.Method == http.MethodGet {
		err := controller.getMarketsHandler(writer, request)
		if err != nil {
			onError(writer, err)
			return
		}
	}
	if request.Method == http.MethodPost {
		err := controller.addMarketHandler(writer, request)
		if err != nil {
			onError(writer, err)
			return
		}
	}

	writer.WriteHeader(http.StatusNotFound)
}
func onError(writer http.ResponseWriter, err error) {
	_ = fmt.Errorf("Error: %v", err)
	writer.WriteHeader(http.StatusInternalServerError)
}

func (controller Controller) ConcreteMarketHandler(writer http.ResponseWriter, request *http.Request) {
	if request.Method == http.MethodGet {
		market, err := controller.getMarketHandler(writer, request)
		if err != nil {
			onError(writer, err)
			return
		}
		writeResponse(market, writer)
		return
	}
	if request.Method == http.MethodPut {
		err := controller.updateMarketHandler(writer, request)
		if err != nil {
			onError(writer, err)
			return
		}
		return
	}
}

func writeResponse(responseObject interface{}, writer http.ResponseWriter) {
	e := json.NewEncoder(writer)
	err := e.Encode(responseObject)
	if err != nil {
		onError(writer, err)
		return
	}
}

func (controller Controller) getMarketHandler(writer http.ResponseWriter, request *http.Request) (Market, error) {
	request.ParseForm()
	ctx := request.Context()
	vars := mux.Vars(request)
	id := vars["id"]
	market, err := controller.repository.GetById(ctx, id)
	return market, err
}

func (controller Controller) updateMarketHandler(writer http.ResponseWriter, request *http.Request) error {
	ctx := request.Context()
	market := controller.getMarketFromQuery(request)
	err := controller.repository.Update(ctx, market)
	return err
}

func (controller Controller) getMarketsHandler(writer http.ResponseWriter, request *http.Request) error {
	defer request.Body.Close()
	ctx := request.Context()
	markets, err := controller.repository.GetAll(ctx)
	if err != nil {
		onError(writer, err)
		return err
	}
	resp, err := json.Marshal(markets)
	if err != nil {
		return err
	}
	_, err = writer.Write(resp)
	return err
}

func (controller Controller) addMarketHandler(writer http.ResponseWriter, request *http.Request) error {
	defer request.Body.Close()
	ctx := request.Context()
	market := controller.getMarketFromQuery(request)
	return controller.repository.Insert(ctx, market)
}

func (controller Controller) getMarketFromQuery(request *http.Request) Market {
	var market = Market{}
	json.NewDecoder(request.Body).Decode(&market)
	return market
}
