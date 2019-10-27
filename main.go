package main

import (
	"context"
	"fmt"
	"github.com/gorilla/mux"
	"go.mongodb.org/mongo-driver/bson"
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

var mongoUrl = os.Getenv("MONGO_URL")

var mongoUser = os.Getenv("MONGO_LOGIN")
var mongoSecret = os.Getenv("MONGO_SECRET")

var redisHost = os.Getenv("REDIS_HOST")
var redisPort = os.Getenv("REDIS_PORT")

func main() {
	nalogruClient := nalogru_client.NalogruClient{BaseAddress: baseAddress, Login: login, Password: password}
	marketsController := markets.New(mongoUrl, mongoUser, mongoSecret)
	ctx := context.Background()
	go sendOdfsRequest(ctx, nalogruClient)
	go getReceipt(ctx, nalogruClient)
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

func getReceipt(ctx context.Context, nalogruClient nalogru_client.NalogruClient) {
	client, err := mongo_client.GetMongoClient(mongoUrl, mongoUser, mongoSecret)
	check(err)

	defer utils.Dispose(func() error {
		return client.Disconnect(ctx)
	}, "error while mongo disconnect")

	collection := client.Database("receipt_collection").Collection("receipt_requests")
	request := receipts.UsersReceipt{}
	err = collection.FindOne(ctx, bson.M{"$and": []bson.M{
		{"odfs_requested": bson.M{"$eq": true}},
		{"receipt": bson.M{"$eq": nil}}},
	}).Decode(&request)

	if err != nil {
		fmt.Printf("error while fetch half-processed user requests. %s", err)
		return
	}
	receiptBytes, err := nalogruClient.SendKktsRequest(request.QueryString)
	check(err)
	receipt, err := receipts.ParseReceipt(receiptBytes)
	check(err)
	filter := bson.M{"_id": bson.M{"$eq": request.Id}}
	update := bson.M{"$set": bson.M{"receipt": receipt}}
	_, err = collection.UpdateOne(ctx, filter, update)
	check(err)
}

func sendOdfsRequest(ctx context.Context, nalogruClient nalogru_client.NalogruClient) {
	client, err := mongo_client.GetMongoClient(mongoUrl, mongoUser, mongoSecret)
	check(err)

	defer utils.Dispose(func() error {
		return client.Disconnect(ctx)
	}, "error while mongo disconnect")

	collection := client.Database("receipt_collection").Collection("receipt_requests")
	usersReceipt := receipts.UsersReceipt{}
	err = collection.FindOne(ctx, bson.M{"odfs_requested": false}).Decode(&usersReceipt)

	if err != nil {
		fmt.Printf("error while fetch unprocessed user requests. %s", err)
		return
	}
	err = nalogruClient.SendOdfsRequest(usersReceipt.QueryString)
	check(err)
	update := bson.M{
		"$set": bson.M{
			"odfs_requested":    true,
			"odfs_request_time": time.Now(),
		},
	}
	filter := bson.M{"_id": bson.M{"$eq": usersReceipt.Id}}
	_, err = collection.UpdateOne(ctx, filter, update)
	check(err)
}

func registerUnauthenticatedRoutes(router *mux.Router) {
	registrationRoute := "/api/user/register"
	router.HandleFunc(registrationRoute, users.UserRegistrationHandler)
	http.Handle(registrationRoute, router)

}

func check(err error) {
	if err != nil {
		panic(err)
	}
}
