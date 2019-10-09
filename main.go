package main

import (
	"context"
	"fmt"
	"github.com/adjust/redismq"
	"github.com/gorilla/mux"
	"go.mongodb.org/mongo-driver/bson"
	"go.mongodb.org/mongo-driver/mongo"
	"net/http"
	"os"
	"receipt_collector/auth"
	"receipt_collector/markets"
	"receipt_collector/mongo_client"
	"receipt_collector/nalogru_client"
	"receipt_collector/receipts"
	"receipt_collector/users"
	"receipt_collector/utils"
	"time"
)

var login = os.Getenv("NALOGRU_LOGIN")
var password = os.Getenv("NALOGRU_PASS")
var baseAddress = os.Getenv("NALOGRU_BASE_ADDR")

const dumpDirectory = "./stub/dump/"

var mongoUrl = os.Getenv("MONGO_URL")

var mongoUser = os.Getenv("MONGO_LOGIN")
var mongoSecret = os.Getenv("MONGO_SECRET")

var redisHost = os.Getenv("REDIS_HOST")
var redisPort = os.Getenv("REDIS_PORT")

var rawReceiptQueue = redismq.CreateQueue(redisHost, redisPort, "", 6, "raw-receipts")
var requestsQueue = redismq.CreateQueue(redisHost, redisPort, "", 6, "requests")

func main() {
	nalogruClient := nalogru_client.NalogruClient{BaseAddress: baseAddress, Login: login, Password: password}
	marketsController := markets.New(mongoUrl, mongoUser, mongoSecret)
	go sendOdfsRequest(nalogruClient)
	go getReceipt(nalogruClient)
	go consumeRawReceipts(rawReceiptQueue)
	router := mux.NewRouter()
	router.HandleFunc("/api/market", marketsController.MarketsBaseHandler)
	router.HandleFunc("/api/market/{id:[a-zA-Z0-9]+}", marketsController.ConcreteMarketHandler).Methods(http.MethodPut, http.MethodGet, http.MethodDelete)
	router.HandleFunc("/api/receipt", receipts.GetReceiptHandler).Methods(http.MethodGet)
	router.HandleFunc("/api/receipt/from-bar-code", receipts.AddReceiptHandler).Methods(http.MethodPost)
	loginRoute := "/api/login"
	router.HandleFunc(loginRoute, users.LoginHandler).Methods(http.MethodPost)
	registerUnauthenticatedRoutes(router)
	http.Handle("/", auth.RequireBasicAuth(router))
	address := ":8888"
	fmt.Printf("Starting http server at: \"%s\"...", address)
	fmt.Println(http.ListenAndServe(address, nil))
}

func getReceipt(nalogruClient nalogru_client.NalogruClient) {
	ctx := context.Background()
	client, err := mongo_client.GetMongoClient(mongoUrl, mongoUser, mongoSecret)
	check(err)

	defer utils.Dispose(func() error {
		return client.Disconnect(ctx)
	}, "error while mongo disconnect")

	collection := client.Database("receipt_collection").Collection("receipt_requests")
	request := receipts.ReceiptRequest{}
	err = collection.FindOne(ctx, bson.M{"odfs_request_time": bson.M{"$ne": nil}}).Decode(&request)

	if err == nil {
		fmt.Printf("error while fetch half-processed user requests. %s", err)
		return
	}
	receiptBytes, err := nalogruClient.SendKktsRequest(request.QueryString)
	check(err)
	receipt, err := receipts.ParseReceipt(receiptBytes)
	userReceipt := receipts.UsersReceipt{
		Receipt: receipt,
		Owner:   request.Owner,
	}
	check(err)
	collection = client.Database("receipt_collection").Collection("user_receipts")
	_, err = collection.InsertOne(ctx, userReceipt)
	check(err)
}

func sendOdfsRequest(nalogruClient nalogru_client.NalogruClient) {
	ctx := context.Background()
	client, err := mongo_client.GetMongoClient(mongoUrl, mongoUser, mongoSecret)
	check(err)

	defer utils.Dispose(func() error {
		return client.Disconnect(ctx)
	}, "error while mongo disconnect")

	collection := client.Database("receipt_collection").Collection("receipt_requests")
	request := receipts.ReceiptRequest{}
	err = collection.FindOne(ctx, bson.M{"odfs_request_time": nil}).Decode(&request)

	if err == nil {
		fmt.Printf("error while fetch unprocessed user requests. %s", err)
		return
	}
	err = nalogruClient.SendOdfsRequest(request.QueryString)
	check(err)
}

func registerUnauthenticatedRoutes(router *mux.Router) {
	registrationRoute := "/api/user/register"
	router.HandleFunc(registrationRoute, users.UserRegistrationHandler)
	http.Handle(registrationRoute, router)

}

func saveResponse(queue *redismq.Queue, response []byte) {
	err := queue.Put(string(response))
	check(err)
}

func check(err error) {
	if err != nil {
		panic(err)
	}
}

func consumeRawReceipts(rawQueue *redismq.Queue) {
	consumer, err := rawQueue.AddConsumer("receipt-parser")
	check(err)
	defer consumer.Quit()
	ctx, _ := context.WithTimeout(context.Background(), 10*time.Second)
	client, err := mongo_client.GetMongoClient(mongoUrl, mongoUser, mongoSecret)
	check(err)
	defer func() {
		err := client.Disconnect(ctx)
		fmt.Printf("error while disconnect %s", err)
	}()
	collection := client.Database("receipt_collection").Collection("receipts")

	if consumer.HasUnacked() {
		unacked, err := consumer.GetUnacked()
		check(err)
		processReceipt(unacked, collection)
		err = unacked.Ack()
		check(err)
	}

	for {
		message, err := consumer.Get()
		check(err)

		processReceipt(message, collection)
		err = message.Ack()
		check(err)
	}
}

func processReceipt(message *redismq.Package, collection *mongo.Collection) {
	ctx, _ := context.WithTimeout(context.Background(), 10*time.Second)
	receipt, err := receipts.ParseReceipt([]byte(message.Payload))
	fmt.Println(receipt.String())
	for i := 0; i < len(receipt.Items); i++ {
		fmt.Println(receipt.Items[i].String())
	}
	_, err = collection.InsertOne(ctx, receipt)
	check(err)

}
