package receipts

import (
	"context"
	"encoding/json"
	"fmt"
	"go.mongodb.org/mongo-driver/bson"
	"go.mongodb.org/mongo-driver/bson/primitive"
	"go.mongodb.org/mongo-driver/mongo"
	"net/http"
	"receipt_collector/auth"
	"receipt_collector/mongo_client"
	utils2 "receipt_collector/utils"
)

type Controller struct {
	mongoUrl      string
	mongoLogin    string
	mongoPassword string
}

func New(mongoUrl string, mongoUser string, mongoSecret string) Controller {
	return Controller{
		mongoUrl:      mongoUrl,
		mongoLogin:    mongoUser,
		mongoPassword: mongoSecret,
	}
}
func OnError(writer http.ResponseWriter, err error) {
	_ = fmt.Errorf("Error: %v", err)
	writer.WriteHeader(http.StatusInternalServerError)
}

func (controller Controller) AddReceiptHandler(writer http.ResponseWriter, request *http.Request) {
	if request.Method != http.MethodPost {
		writer.WriteHeader(http.StatusNotFound)
		return
	}
	defer func() {
		err := request.Body.Close()
		if err != nil {
			fmt.Printf("error while request body close %s", err)
		}
	}()

	err := controller.saveRequest(request)
	if err != nil {
		OnError(writer, err)
		return
	}
}

func (controller Controller) getMongoClient() (*mongo.Client, error) {
	return mongo_client.GetMongoClient(controller.mongoUrl, controller.mongoLogin, controller.mongoPassword)
}

func (controller Controller) saveRequest(request *http.Request) error {
	queryString := request.URL.RawQuery
	ctx := request.Context()
	defer utils2.Dispose(request.Body.Close, "error while request body close")

	client, err := controller.getMongoClient()
	if err != nil {
		return err
	}
	defer utils2.Dispose(func() error {
		return client.Disconnect(ctx)
	}, "error while mongo disconnect")

	collection := client.Database("receipt_collection").Collection("receipt_requests")
	userId := ctx.Value(auth.UserId)

	id, err := primitive.ObjectIDFromHex(userId.(string))
	if err != nil {
		return err
	}
	receiptRequest := UsersReceipt{
		Owner:         id,
		QueryString:   queryString,
		OdfsRequested: false,
	}
	_, err = collection.InsertOne(ctx, receiptRequest)
	return err
}

func (controller Controller) GetReceiptHandler(writer http.ResponseWriter, request *http.Request) {
	if request.Method != http.MethodGet {
		writer.WriteHeader(http.StatusNotFound)
		return
	}
	ctx := request.Context()
	defer utils2.Dispose(request.Body.Close, "error while request body close")

	client, err := controller.getMongoClient()
	if err != nil {
		OnError(writer, err)
		return
	}

	defer utils2.Dispose(func() error {
		return client.Disconnect(ctx)
	}, "error while mongo disconnect")

	collection := client.Database("receipt_collection").Collection("receipt_requests")
	userId := ctx.Value(auth.UserId)
	id, err := primitive.ObjectIDFromHex(userId.(string))
	if err != nil {
		OnError(writer, err)
		return
	}
	cursor, err := collection.Find(ctx, bson.D{{"owner", id}})
	if err != nil {
		OnError(writer, err)
		return
	}
	defer utils2.Dispose(func() error {
		return cursor.Close(ctx)
	}, "error while mongo cursor close")
	var receipts = readReceipts(cursor, ctx)
	resp, err := json.Marshal(receipts)
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

func readReceipts(cursor *mongo.Cursor, context context.Context) []UsersReceipt {
	var receipts = make([]UsersReceipt, 0, 0)
	for cursor.Next(context) {
		var receipt UsersReceipt
		err := cursor.Decode(&receipt)
		check(err)
		receipts = append(receipts, receipt)
	}
	return receipts
}

func check(err error) {
	if err != nil {
		panic(err)
	}
}
