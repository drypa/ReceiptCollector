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
		err := controller.addMarketHandler(request)
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
		market, err := controller.getMarketHandler(request)
		if err != nil {
			onError(writer, err)
			return
		}
		writeResponse(market, writer)
		return
	}
	if request.Method == http.MethodPut {
		err := controller.updateMarketHandler(request)
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

func (controller Controller) getMarketHandler(request *http.Request) (*Market, error) {
	err := request.ParseForm()
	if err != nil {
		return nil, err
	}

	ctx := request.Context()
	vars := mux.Vars(request)
	id := vars["id"]
	market, err := controller.repository.GetById(ctx, id)
	return &market, err
}

func (controller Controller) updateMarketHandler(request *http.Request) error {
	ctx := request.Context()
	market, err := controller.getMarketFromQuery(request)
	if err != nil {
		return err
	}
	err = controller.repository.Update(ctx, *market)
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

func (controller Controller) addMarketHandler(request *http.Request) error {
	defer request.Body.Close()
	ctx := request.Context()
	market, err := controller.getMarketFromQuery(request)
	if err != nil {
		return err
	}
	return controller.repository.Insert(ctx, *market)
}

func (controller Controller) getMarketFromQuery(request *http.Request) (*Market, error) {
	var market = Market{}
	err := json.NewDecoder(request.Body).Decode(&market)
	return &market, err
}
