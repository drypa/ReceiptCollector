package waste

import (
	"context"
	"encoding/json"
	"fmt"
	"net/http"
	"receipt_collector/auth"
	"strconv"
	"time"
)

//Controller of wastes.
type Controller struct {
	repository Repository
}

//New creates Controller instance.
func New(repository Repository) Controller {
	return Controller{
		repository: repository,
	}
}

//GetHandler provides get-wastes api method.
func (controller Controller) GetHandler(writer http.ResponseWriter, request *http.Request) {
	ctx := request.Context()
	filter, err := getFilterFromQuery(request)
	if err != nil {
		onError(writer, err)
		return
	}
	query := mapFilter(ctx, filter)
	receipts, err := controller.repository.GetByFilter(ctx, query)
	if err != nil {
		onError(writer, err)
		return
	}
	writeResponse(receipts, writer)
}
func getUserId(ctx context.Context) string {
	userId := ctx.Value(auth.UserId)
	return userId.(string)
}

func mapFilter(ctx context.Context, filter Filter) QueryFilter {
	result := QueryFilter{UserId: getUserId(ctx)}
	if filter.From != 0 {
		from := time.Unix(0, filter.From*int64(time.Millisecond))
		result.From = &from
	}

	if filter.To != 0 {
		to := time.Unix(0, filter.To*int64(time.Millisecond))
		result.To = &to
	}
	return result
}

func onError(writer http.ResponseWriter, err error) {
	_ = fmt.Errorf("Error: %v", err)
	writer.WriteHeader(http.StatusInternalServerError)
}

func getFilterFromQuery(request *http.Request) (Filter, error) {
	var filter = Filter{}
	query := request.URL.Query()
	from, err := strconv.ParseInt(query.Get("from"), 10, 64)
	if err != nil {
		return filter, err
	}
	filter.From = from

	to, err := strconv.ParseInt(query.Get("to"), 10, 64)
	if err != nil {
		return filter, err
	}
	filter.To = to

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

type Filter struct {
	From int64
	To   int64
}
