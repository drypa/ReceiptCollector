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
	"receipt_collector/dispose"
	"receipt_collector/nalogru"
	"receipt_collector/nalogru/qr"
	"strings"
)

type Controller struct {
	repository    Repository
	nalogruClient *nalogru.Client
}

//New creates controller.
func New(repository Repository, nalogruClient *nalogru.Client) Controller {
	return Controller{
		repository:    repository,
		nalogruClient: nalogruClient,
	}
}
func onError(writer http.ResponseWriter, err error) {
	log.Printf("Error: %v", err)
	writer.WriteHeader(http.StatusInternalServerError)
}

func (controller Controller) AddReceiptHandler(writer http.ResponseWriter, request *http.Request) {
	defer dispose.Dispose(request.Body.Close, "error while request body close")
	ctx := request.Context()
	userId := ctx.Value(auth.UserId).(string)
	queryString := request.URL.RawQuery
	err := processReceiptQueryString(ctx, &controller.repository, queryString, userId)
	if err != nil {
		onError(writer, err)
		return
	}
}

func processReceiptQueryString(ctx context.Context,
	r *Repository,
	queryString string,
	userId string) error {
	receiptString := strings.TrimSpace(queryString)
	result, err := qr.Parse(receiptString)
	if err != nil {
		return err
	}

	err = saveRequest(ctx, r, result.ToString(), userId)
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
		onError(writer, err)
		return
	}
	for _, v := range receiptStrings {
		err := processReceiptQueryString(ctx, &controller.repository, v, userId)
		if err != nil {
			log.Printf("error processing %s with error %v\n", v, err)
			onError(writer, err)
			return
		}
	}
}

func saveRequest(ctx context.Context, r *Repository, queryString string, userId string) error {

	id, err := primitive.ObjectIDFromHex(userId)
	if err != nil {
		return err
	}

	receipt, err := r.GetByQueryString(ctx, userId, queryString)
	if err != nil {
		return err
	}

	if receipt != nil {
		return errors.New("already exist")
	}

	receiptRequest := UsersReceipt{
		Owner:       id,
		QueryString: queryString,
	}
	err = r.Insert(ctx, receiptRequest)
	return err
}

func (controller Controller) GetReceiptsHandler(writer http.ResponseWriter, request *http.Request) {
	ctx := request.Context()
	defer dispose.Dispose(request.Body.Close, "error while request body close")

	userId := ctx.Value(auth.UserId)

	if userId == nil {
		userId = "5dc1c9427126cc2841ca384d"
	}

	receipts, err := controller.repository.GetByUser(ctx, userId.(string))
	if err != nil {
		onError(writer, err)
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
		onError(writer, err)
		return
	}
	writeResponse(receipt, writer)
}

func getUserId(ctx context.Context) string {
	userId := ctx.Value(auth.UserId)
	if userId == nil {
		userId = "5dc1c9427126cc2841ca384d"
	}
	return userId.(string)
}

func getFromQuery(paramName string, request *http.Request) (string, error) {
	//TODO: move to base controller
	err := request.ParseForm()
	if err != nil {
		return "", err
	}
	vars := mux.Vars(request)
	id := vars[paramName]
	return id, nil
}

func getReceiptId(writer http.ResponseWriter, request *http.Request) string {
	id, err := getFromQuery("id", request)
	if err != nil {
		onError(writer, err)
		return ""
	}
	return id
}

func (controller Controller) DeleteReceiptHandler(writer http.ResponseWriter, request *http.Request) {
	ctx := request.Context()

	defer dispose.Dispose(request.Body.Close, "error while request body close")

	id := getReceiptId(writer, request)

	userId := ctx.Value(auth.UserId)
	err := controller.repository.Delete(ctx, userId.(string), id)
	if err != nil {
		onError(writer, err)
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

func (controller Controller) getReceiptById(ctx context.Context, userId string, receiptId string) (UsersReceipt, error) {
	return controller.repository.GetById(ctx, userId, receiptId)
}

func (controller Controller) AddReceiptForTelegramUserHandler(writer http.ResponseWriter, request *http.Request) {
	ctx := request.Context()
	defer dispose.Dispose(request.Body.Close, "error while request body close")
	receiptRequest := addReceiptRequest{}
	err := getFromBody(request, &receiptRequest)
	if err != nil {
		onError(writer, err)
		return
	}
	err = processReceiptQueryString(ctx, &controller.repository, receiptRequest.ReceiptString, receiptRequest.UserId)
	if err != nil {
		onError(writer, err)
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
