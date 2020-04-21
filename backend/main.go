package main

import (
	"context"
	"github.com/gorilla/mux"
	"go.mongodb.org/mongo-driver/mongo"
	"google.golang.org/grpc/credentials"
	"log"
	"net/http"
	"os"
	"receipt_collector/auth"
	"receipt_collector/dispose"
	"receipt_collector/internal"
	"receipt_collector/markets"
	"receipt_collector/mongo_client"
	"receipt_collector/nalogru"
	"receipt_collector/receipts"
	"receipt_collector/users"
	"receipt_collector/users/login_url"
	"receipt_collector/workers"
)

var login = os.Getenv("NALOGRU_LOGIN")
var password = os.Getenv("NALOGRU_PASS")
var baseAddress = os.Getenv("NALOGRU_BASE_ADDR")

var mongoURL = os.Getenv("MONGO_URL")

var mongoUser = os.Getenv("MONGO_LOGIN")
var mongoSecret = os.Getenv("MONGO_SECRET")
var openUrl = os.Getenv("OPEN_URL")

func main() {
	log.SetOutput(os.Stdout)
	settings := workers.ReadFromEnvironment()
	log.Printf("Worker settings %v \n", settings)

	nalogruClient := nalogru.Client{BaseAddress: baseAddress, Login: login, Password: password}
	ctx := context.Background()
	client, err := getMongoClient()
	if err != nil {
		check(err)
	}
	defer dispose.Dispose(func() error {
		return client.Disconnect(context.Background())
	}, "error while mongo disconnect")
	receiptRepository := receipts.NewRepository(client)
	userRepository := users.NewRepository(client)
	marketRepository := markets.NewRepository(client)
	worker := workers.New(nalogruClient, receiptRepository)

	go worker.OdfsStart(ctx, settings)
	go worker.GetReceiptStart(ctx, settings)
	generator := login_url.New(openUrl)

	creds, err := credentials.NewServerTLSFromFile("../ssl/certificate.crt", "../ssl/private.key")
	if err != nil {
		log.Fatalf("failed to load TLS keys: %v", err)
	}
	var processor internal.Processor = login_url.NewLoginLinkProcessor(&userRepository, generator)
	internal.Serve(":15000", creds, &processor)
	log.Println(startServer(nalogruClient, receiptRepository, userRepository, marketRepository, generator))
}

func getMongoClient() (*mongo.Client, error) {
	settings := mongo_client.NewSettings(mongoURL, mongoUser, mongoSecret)
	return mongo_client.New(settings)
}

func startServer(nalogruClient nalogru.Client,
	receiptRepository receipts.Repository,
	userRepository users.Repository,
	marketRepository markets.Repository,
	generator users.LinkGenerator) error {

	marketsController := markets.New(marketRepository)

	receiptsController := receipts.New(receiptRepository, nalogruClient)
	usersController := users.New(userRepository, generator)
	basicAuth := auth.New(userRepository)
	router := mux.NewRouter()
	router.HandleFunc("/api/market", marketsController.MarketsBaseHandler)
	router.HandleFunc("/api/market/{id:[a-zA-Z0-9]+}", marketsController.ConcreteMarketHandler).Methods(http.MethodPut, http.MethodGet, http.MethodDelete)
	router.HandleFunc("/api/receipt", receiptsController.GetReceiptsHandler).Methods(http.MethodGet)
	router.HandleFunc("/api/receipt/{id:[a-zA-Z0-9]+}", receiptsController.GetReceiptDetailsHandler).Methods(http.MethodGet)
	router.HandleFunc("/api/receipt/{id:[a-zA-Z0-9]+}/odfs", receiptsController.OdfsRequestHandler).Methods(http.MethodPost)
	router.HandleFunc("/api/receipt/{id:[a-zA-Z0-9]+}/kkts", receiptsController.KktsRequestHandler).Methods(http.MethodPost)
	router.HandleFunc("/api/receipt/{id:[a-zA-Z0-9]+}", receiptsController.DeleteReceiptHandler).Methods(http.MethodDelete)
	router.HandleFunc("/api/receipt/from-bar-code", receiptsController.AddReceiptHandler).Methods(http.MethodPost)
	router.HandleFunc("/api/receipt/batch", receiptsController.BatchAddReceiptHandler).Methods(http.MethodPost)
	loginRoute := "/api/login"
	router.HandleFunc(loginRoute, usersController.LoginHandler).Methods(http.MethodPost)
	registerUnauthenticatedRoutes(router, usersController, receiptsController)
	http.Handle("/", basicAuth.RequireBasicAuth(router))
	address := ":8888"
	log.Printf("Starting http server at: \"%s\"...", address)
	return http.ListenAndServe(address, nil)
}

func registerUnauthenticatedRoutes(router *mux.Router, controller users.Controller, receiptsController receipts.Controller) {
	registrationRoute := "/api/user/register"
	registrationByTelegramRoute := "/internal/account"
	getUsersRoute := "/internal/account"
	addReceiptRoute := "/internal/receipt"
	getLoginUrlRoute := "/internal/{id:[0-9]+}/login-link"
	router.HandleFunc(registrationRoute, controller.UserRegistrationHandler).Methods(http.MethodPost)
	router.HandleFunc(getLoginUrlRoute, controller.GetLoginUrlHandler).Methods(http.MethodGet)
	router.HandleFunc(registrationByTelegramRoute, controller.GetUserByTelegramIdHandler).Methods(http.MethodPost)
	router.HandleFunc(getUsersRoute, controller.GetUsersHandler).Methods(http.MethodGet)

	router.HandleFunc(addReceiptRoute, receiptsController.AddReceiptForTelegramUserHandler).Methods(http.MethodPost)

	http.Handle(registrationRoute, router)
	http.Handle("/internal/", router)
}

func check(err error) {
	if err != nil {
		log.Printf("Error occurred %v", err)
		panic(err)
	}
}
