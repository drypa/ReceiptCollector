package receipts

import (
	"context"
	"encoding/json"
	"errors"
	"github.com/gorilla/mux"
	"go.mongodb.org/mongo-driver/bson/primitive"
	"log"
	"net/http"
	"receipt_collector/auth"
	"receipt_collector/nalogru"
	utils2 "receipt_collector/utils"
)

type Controller struct {
	repository    Repository
	nalogruClient nalogru.Client
}

func New(repository Repository, nalogruClient nalogru.Client) Controller {
	return Controller{
		repository:    repository,
		nalogruClient: nalogruClient,
	}
}
func OnError(writer http.ResponseWriter, err error) {
	log.Printf("Error: %v", err)
	writer.WriteHeader(http.StatusInternalServerError)
}

func (controller Controller) AddReceiptHandler(writer http.ResponseWriter, request *http.Request) {
	defer utils2.Dispose(request.Body.Close, "error while request body close")

	queryString := request.URL.RawQuery
	result, err := nalogru.Parse(queryString)
	if err != nil {
		OnError(writer, err)
		return
	}

	err = result.Validate()
	if err != nil {
		OnError(writer, err)
		return
	}

	err = controller.saveRequest(request.Context(), queryString)
	if err != nil {
		OnError(writer, err)
		return
	}
}

func (controller Controller) saveRequest(ctx context.Context, queryString string) error {

	userId := ctx.Value(auth.UserId)

	id, err := primitive.ObjectIDFromHex(userId.(string))
	if err != nil {
		return err
	}

	receipt, err := controller.repository.GetByQueryString(ctx, userId.(string), queryString)
	if err != nil {
		return err
	}

	if receipt != nil {
		return errors.New("already exist")
	}

	receiptRequest := UsersReceipt{
		Owner:             id,
		QueryString:       queryString,
		OdfsRequestStatus: Undefined,
	}
	err = controller.repository.Insert(ctx, receiptRequest)
	return err
}

func (controller Controller) GetReceiptsHandler(writer http.ResponseWriter, request *http.Request) {
	if request.Method != http.MethodGet {
		writer.WriteHeader(http.StatusNotFound)
		return
	}
	ctx := request.Context()
	defer utils2.Dispose(request.Body.Close, "error while request body close")

	userId := ctx.Value(auth.UserId)
	receipts, err := controller.repository.GetByUser(ctx, userId.(string))
	if err != nil {
		OnError(writer, err)
		return
	}
	writeResponse(receipts, writer)
}

func (controller Controller) GetReceiptDetailsHandler(writer http.ResponseWriter, request *http.Request) {
	ctx := request.Context()

	defer utils2.Dispose(request.Body.Close, "error while request body close")

	id := getReceiptId(writer, request)
	userId := getUserId(ctx)
	receipt, err := controller.getReceiptById(ctx, userId, id)
	if err != nil {
		OnError(writer, err)
		return
	}
	writeResponse(receipt, writer)
}

func getUserId(ctx context.Context) string {
	userId := ctx.Value(auth.UserId)
	return userId.(string)
}

func getReceiptId(writer http.ResponseWriter, request *http.Request) string {
	err := request.ParseForm()
	if err != nil {
		OnError(writer, err)
		return ""
	}
	vars := mux.Vars(request)
	id := vars["id"]
	return id
}

func (controller Controller) DeleteReceiptHandler(writer http.ResponseWriter, request *http.Request) {
	ctx := request.Context()

	defer utils2.Dispose(request.Body.Close, "error while request body close")

	id := getReceiptId(writer, request)

	userId := ctx.Value(auth.UserId)
	err := controller.repository.Delete(ctx, userId.(string), id)
	if err != nil {
		OnError(writer, err)
		return
	}
}
func writeResponse(responseObject interface{}, writer http.ResponseWriter) {
	resp, err := json.Marshal(responseObject)
	if err != nil {
		OnError(writer, err)
		return
	}
	_, err = writer.Write(resp)
	if err != nil {
		OnError(writer, err)
		return
	}
}

func (controller Controller) getReceiptById(ctx context.Context, userId string, receiptId string) (UsersReceipt, error) {
	return controller.repository.GetById(ctx, userId, receiptId)
}

func (controller Controller) OdfsRequestHandler(writer http.ResponseWriter, request *http.Request) {
	ctx := request.Context()

	defer utils2.Dispose(request.Body.Close, "error while request body close")

	receiptId := getReceiptId(writer, request)
	userId := getUserId(ctx)
	receipt, err := controller.repository.GetById(ctx, userId, receiptId)
	if err != nil {
		OnError(writer, err)
		return
	}
	err = controller.nalogruClient.SendOdfsRequest(receipt.QueryString)
	writeResponse(err.Error(), writer)
}

func (controller Controller) KktsRequestHandler(writer http.ResponseWriter, request *http.Request) {

}

func check(err error) {
	if err != nil {
		log.Printf("Error occured %v", err)
		panic(err)
	}
}
