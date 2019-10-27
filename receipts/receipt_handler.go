package receipts

import (
	"context"
	"encoding/json"
	"fmt"
	"github.com/adjust/redismq"
	"go.mongodb.org/mongo-driver/bson"
	"go.mongodb.org/mongo-driver/bson/primitive"
	"go.mongodb.org/mongo-driver/mongo"
	"net/http"
	"os"
	"receipt_collector/auth"
	"receipt_collector/mongo_client"
	"receipt_collector/utils"
)

var mongoUrl = os.Getenv("MONGO_URL")

var mongoUser = os.Getenv("MONGO_LOGIN")
var mongoSecret = os.Getenv("MONGO_SECRET")

var redisHost = os.Getenv("REDIS_HOST")
var redisPort = os.Getenv("REDIS_PORT")

var requestsQueue = redismq.CreateQueue(redisHost, redisPort, "", 6, "requests")

func AddReceiptHandler(writer http.ResponseWriter, request *http.Request) {
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

	err := saveRequest(request)
	if err != nil {
		fmt.Printf("error while save user request. %s", err)
		writer.WriteHeader(http.StatusInternalServerError)
		return
	}
}

func saveRequest(request *http.Request) error {
	queryString := request.URL.RawQuery
	ctx := request.Context()
	defer utils.Dispose(request.Body.Close, "error while request body close")

	client, err := mongo_client.GetMongoClient(mongoUrl, mongoUser, mongoSecret)
	if err != nil {
		return err
	}
	defer utils.Dispose(func() error {
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

func GetReceiptHandler(writer http.ResponseWriter, request *http.Request) {
	if request.Method != http.MethodGet {
		writer.WriteHeader(http.StatusNotFound)
		return
	}
	ctx := request.Context()
	defer utils.Dispose(request.Body.Close, "error while request body close")

	client, err := mongo_client.GetMongoClient(mongoUrl, mongoUser, mongoSecret)
	if err != nil {
		writer.WriteHeader(http.StatusInternalServerError)
		return
	}

	defer utils.Dispose(func() error {
		return client.Disconnect(ctx)
	}, "error while mongo disconnect")

	collection := client.Database("receipt_collection").Collection("receipt_requests")
	userId := ctx.Value(auth.UserId)
	id, err := primitive.ObjectIDFromHex(userId.(string))
	if err != nil {
		writer.WriteHeader(http.StatusInternalServerError)
		return
	}
	cursor, err := collection.Find(ctx, bson.D{{"owner", id}})
	if err != nil {
		writer.WriteHeader(http.StatusInternalServerError)
		return
	}
	defer utils.Dispose(func() error {
		return cursor.Close(ctx)
	}, "error while mongo cursor close")
	var receipts = readReceipts(cursor, ctx)
	resp, err := json.Marshal(receipts)
	if err != nil {
		writer.WriteHeader(http.StatusInternalServerError)
		return
	}
	_, err = writer.Write(resp)
	if err != nil {
		writer.WriteHeader(http.StatusInternalServerError)
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
