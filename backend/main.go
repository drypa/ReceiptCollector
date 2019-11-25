package main

import (
	"context"
	"github.com/gorilla/mux"
	"go.mongodb.org/mongo-driver/mongo"
	"log"
	"net/http"
	"os"
	"receipt_collector/auth"
	"receipt_collector/markets"
	"receipt_collector/mongo_client"
	"receipt_collector/nalogru"
	"receipt_collector/receipts"
	"receipt_collector/users"
	"receipt_collector/utils"
	"receipt_collector/workers"
)

var login = os.Getenv("NALOGRU_LOGIN")
var password = os.Getenv("NALOGRU_PASS")
var baseAddress = os.Getenv("NALOGRU_BASE_ADDR")

var mongoUrl = os.Getenv("MONGO_URL")

var mongoUser = os.Getenv("MONGO_LOGIN")
var mongoSecret = os.Getenv("MONGO_SECRET")

func main() {
	log.SetOutput(os.Stdout)
	settings := workers.ReadFromEnvironment()
	log.Printf("Worker settings %v \n", settings)

	nalogruClient := nalogru.Client{BaseAddress: baseAddress, Login: login, Password: password}
	ctx := context.Background()
	client, err := getMongoClient(mongoUrl, mongoUser, mongoSecret)
	if err != nil {
		check(err)
	}
	defer utils.Dispose(func() error {
		return client.Disconnect(context.Background())
	}, "error while mongo disconnect")

	go workers.OdfsWorkerStart(ctx, nalogruClient, client, settings)
	go workers.GetReceiptWorkerStart(ctx, nalogruClient, client, settings)

	log.Println(startServer())
}

func getMongoClient(mongoUrl string, mongoLogin string, mongoPassword string) (*mongo.Client, error) {
	return mongo_client.GetMongoClient(mongoUrl, mongoLogin, mongoPassword)
}

func startServer() error {
	marketsController := markets.New(mongoUrl, mongoUser, mongoSecret)
	client, err := getMongoClient(mongoUrl, mongoUser, mongoSecret)
	if err != nil {
		return err
	}
	defer utils.Dispose(func() error {
		return client.Disconnect(context.Background())
	}, "error while mongo disconnect")

	repository := receipts.NewRepository(client)
	receiptsController := receipts.New(repository)
	usersController := users.New(mongoUrl, mongoUser, mongoSecret)
	router := mux.NewRouter()
	router.HandleFunc("/api/market", marketsController.MarketsBaseHandler)
	router.HandleFunc("/api/market/{id:[a-zA-Z0-9]+}", marketsController.ConcreteMarketHandler).Methods(http.MethodPut, http.MethodGet, http.MethodDelete)
	router.HandleFunc("/api/receipt", receiptsController.GetReceiptsHandler).Methods(http.MethodGet)
	router.HandleFunc("/api/receipt/{id:[a-zA-Z0-9]+}", receiptsController.GetReceiptDetailsHandler).Methods(http.MethodGet)
	router.HandleFunc("/api/receipt/{id:[a-zA-Z0-9]+}", receiptsController.DeleteReceiptHandler).Methods(http.MethodDelete)
	router.HandleFunc("/api/receipt/from-bar-code", receiptsController.AddReceiptHandler).Methods(http.MethodPost)
	loginRoute := "/api/login"
	router.HandleFunc(loginRoute, usersController.LoginHandler).Methods(http.MethodPost)
	registerUnauthenticatedRoutes(router, usersController)
	http.Handle("/", auth.RequireBasicAuth(router))
	address := ":8888"
	log.Printf("Starting http server at: \"%s\"...", address)
	return http.ListenAndServe(address, nil)
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
