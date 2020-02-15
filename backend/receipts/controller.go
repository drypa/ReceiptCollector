package receipts

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"github.com/gorilla/mux"
	"go.mongodb.org/mongo-driver/bson/primitive"
	"log"
	"net/http"
	"receipt_collector/auth"
	"receipt_collector/dispose"
	"receipt_collector/nalogru"
	"strings"
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
	defer dispose.Dispose(request.Body.Close, "error while request body close")
	ctx := request.Context()
	userId := ctx.Value(auth.UserId).(string)
	queryString := request.URL.RawQuery
	err := controller.processReceiptQueryString(ctx, queryString, userId)
	if err != nil {
		OnError(writer, err)
		return
	}
}

func (controller Controller) processReceiptQueryString(ctx context.Context,
	queryString string,
	userId string) error {
	receiptString := strings.TrimSpace(queryString)
	result, err := nalogru.Parse(receiptString)
	if err != nil {
		return err
	}

	err = result.Validate()
	if err != nil {
		return err
	}

	err = controller.saveRequest(ctx, receiptString, userId)
	if err != nil {
		return err
	}
	return nil
}

//BatchAddReceiptHandler allow add many receipts at same time.
func (controller Controller) BatchAddReceiptHandler(writer http.ResponseWriter, request *http.Request) {
	defer dispose.Dispose(request.Body.Close, "error while request body close")
	ctx := request.Context()
	userId := ctx.Value(auth.UserId).(string)
	receiptStrings := make([]string, 0, 0)

	decoder := json.NewDecoder(request.Body)
	err := decoder.Decode(&receiptStrings)
	if err != nil {
		OnError(writer, err)
		return
	}
	for _, v := range receiptStrings {
		err := controller.processReceiptQueryString(ctx, v, userId)
		if err != nil {
			log.Printf("error processing %s with error %v\n", v, err)
			OnError(writer, err)
			return
		}
	}
}

func (controller Controller) saveRequest(ctx context.Context, queryString string, userId string) error {

	id, err := primitive.ObjectIDFromHex(userId)
	if err != nil {
		return err
	}

	receipt, err := controller.repository.GetByQueryString(ctx, userId, queryString)
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
	ctx := request.Context()
	defer dispose.Dispose(request.Body.Close, "error while request body close")

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

	defer dispose.Dispose(request.Body.Close, "error while request body close")

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

	defer dispose.Dispose(request.Body.Close, "error while request body close")

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

	defer dispose.Dispose(request.Body.Close, "error while request body close")

	receiptId := getReceiptId(writer, request)
	userId := getUserId(ctx)
	receipt, err := controller.repository.GetById(ctx, userId, receiptId)
	if err != nil {
		OnError(writer, err)
		return
	}
	err = controller.nalogruClient.SendOdfsRequest(receipt.QueryString)
	if err != nil {
		writeResponse(err.Error(), writer)
	}
}

func (controller Controller) KktsRequestHandler(writer http.ResponseWriter, request *http.Request) {
	ctx := request.Context()
	defer dispose.Dispose(request.Body.Close, "error while request body close")

	receiptId := getReceiptId(writer, request)
	userId := getUserId(ctx)
	receipt, err := controller.repository.GetById(ctx, userId, receiptId)
	if err != nil {
		OnError(writer, err)
		return
	}
	response, err := controller.nalogruClient.SendKktsRequest(receipt.QueryString)

	if err != nil {
		writer.Write([]byte(err.Error()))
		return
	}
	controller.trySaveReceipt(ctx, response, writer, receipt)
}

func (controller Controller) trySaveReceipt(ctx context.Context, response []byte, writer http.ResponseWriter, receipt UsersReceipt) {
	receiptFromApi, err := ParseReceipt(response)
	if err != nil {
		body := string(response)
		if body == nalogru.TicketNotFound {
			controller.repository.SetKktsRequestStatus(ctx, receipt.Id.Hex(), NotFound)
			return
		}
		result := fmt.Sprintf("Can not parse response body.Body: '%s'.Error: %v", body, err)
		writeResponse(result, writer)
		return
	}
	err = controller.repository.SetReceipt(ctx, receipt.Id, receiptFromApi)
	if err != nil {
		writeResponse(err.Error(), writer)
	}
}

func (controller Controller) AddReceiptForTelegramUserHandler(writer http.ResponseWriter, request *http.Request) {
	ctx := request.Context()
	defer dispose.Dispose(request.Body.Close, "error while request body close")
	receiptRequest := addReceiptRequest{}
	err := getFromBody(request, &receiptRequest)
	if err != nil {
		OnError(writer, err)
		return
	}
	err = controller.processReceiptQueryString(ctx, receiptRequest.ReceiptString, receiptRequest.UserId)
	if err != nil {
		OnError(writer, err)
		return
	}
}

func getFromBody(r *http.Request, result interface{}) error {
	err := json.NewDecoder(r.Body).Decode(result)
	if err != nil {
		return err
	}
	return nil
}

func check(err error) {
	if err != nil {
		log.Printf("Error occurred %v", err)
		panic(err)
	}
}
