package main

import (
	"context"
	"github.com/gorilla/mux"
	"go.mongodb.org/mongo-driver/bson"
	"go.mongodb.org/mongo-driver/mongo"
	"log"
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

const intervalEnvironmentVariable = "GET_RECEIPT_WORKER_INTERVAL"

var workerIntervalString = os.Getenv(intervalEnvironmentVariable)

func main() {
	log.SetOutput(os.Stdout)

	nalogruClient := nalogru_client.Client{BaseAddress: baseAddress, Login: login, Password: password}
	ctx := context.Background()

	processingInterval, err := time.ParseDuration(workerIntervalString)
	if err != nil {
		log.Printf("invalid '%s' value: %s", intervalEnvironmentVariable, workerIntervalString)
	}

	go sendOdfsRequestWorkerStart(ctx, nalogruClient, processingInterval)
	go startGetReceiptWorker(ctx, nalogruClient, processingInterval)

	log.Println(startServer())
}

func startServer() error {
	marketsController := markets.New(mongoUrl, mongoUser, mongoSecret)
	receiptsController := receipts.New(mongoUrl, mongoUser, mongoSecret)
	usersController := users.New(mongoUrl, mongoUser, mongoSecret)
	router := mux.NewRouter()
	router.HandleFunc("/api/market", marketsController.MarketsBaseHandler)
	router.HandleFunc("/api/market/{id:[a-zA-Z0-9]+}", marketsController.ConcreteMarketHandler).Methods(http.MethodPut, http.MethodGet, http.MethodDelete)
	router.HandleFunc("/api/receipt", receiptsController.GetReceiptsHandler).Methods(http.MethodGet)
	router.HandleFunc("/api/receipt/{id:[a-zA-Z0-9]+}", receiptsController.GetReceiptDetailsHandler).Methods(http.MethodGet)
	router.HandleFunc("/api/receipt/from-bar-code", receiptsController.AddReceiptHandler).Methods(http.MethodPost)
	loginRoute := "/api/login"
	router.HandleFunc(loginRoute, usersController.LoginHandler).Methods(http.MethodPost)
	registerUnauthenticatedRoutes(router, usersController)
	http.Handle("/", auth.RequireBasicAuth(router))
	address := ":8888"
	log.Printf("Starting http server at: \"%s\"...", address)
	return http.ListenAndServe(address, nil)
}

func startGetReceiptWorker(ctx context.Context, nalogruClient nalogru_client.Client, interval time.Duration) {
	ticker := time.NewTicker(interval)

	for {
		select {
		case <-ctx.Done():
			log.Println("Kkt request worker finished")
			return
		case <-ticker.C:
			hour := time.Now().Hour()
			if hour > 22 || hour < 5 {
				getReceipt(ctx, nalogruClient)
			} else {
				log.Print("Not Yet")
				break
			}
			break
		}
	}

}

func getReceipt(ctx context.Context, nalogruClient nalogru_client.Client) {
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

	if err == mongo.ErrNoDocuments {
		log.Println("No Kkt requests required")
		return
	}

	if err != nil {
		log.Printf("error while fetch half-processed user requests. %s", err)
		return
	}
	receiptBytes, err := nalogruClient.SendKktsRequest(request.QueryString)
	check(err)
	receipt, err := receipts.ParseReceipt(receiptBytes)
	if err != nil {
		body := string(receiptBytes)
		log.Printf("Can not parse response body.Body: '%s'.Error: %v", body, err)
		return
	}
	filter := bson.M{"_id": bson.M{"$eq": request.Id}}
	update := bson.M{"$set": bson.M{"receipt": receipt}}
	_, err = collection.UpdateOne(ctx, filter, update)
	check(err)
}

func sendOdfsRequestWorkerStart(ctx context.Context, nalogruClient nalogru_client.Client, interval time.Duration) {
	ticker := time.NewTicker(interval)

	for {
		select {
		case <-ctx.Done():
			log.Println("Odfs request worker finished")
			return
		case <-ticker.C:
			processRequests(ctx, nalogruClient)
			break
		}
	}

}

func processRequests(ctx context.Context, nalogruClient nalogru_client.Client) {
	client, err := mongo_client.GetMongoClient(mongoUrl, mongoUser, mongoSecret)
	check(err)

	defer utils.Dispose(func() error {
		return client.Disconnect(ctx)
	}, "error while mongo disconnect")

	collection := client.Database("receipt_collection").Collection("receipt_requests")
	usersReceipt := receipts.UsersReceipt{}
	err = collection.FindOne(ctx, bson.M{"odfs_requested": false}).Decode(&usersReceipt)
	if err == mongo.ErrNoDocuments {
		log.Println("No Odfs requests required")
		return
	}
	if err != nil {
		log.Printf("error while fetch unprocessed user requests. %s \n", err)
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

func registerUnauthenticatedRoutes(router *mux.Router, controller users.Controller) {
	registrationRoute := "/api/user/register"
	router.HandleFunc(registrationRoute, controller.UserRegistrationHandler)
	http.Handle(registrationRoute, router)

}

func check(err error) {
	if err != nil {
		log.Printf("Error occured %v", err)
		panic(err)
	}
}
